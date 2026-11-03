/*
 * Lanceur du rendu PDF isolé (SOC-05), seul programme setuid de l'image (propriétaire root, mode 4750, groupe de l'API).
 *
 * L'API l'exécute avec les quatre arguments numériques du rendu (page, côté, surface, mémoire). Il lance le processus de
 * rendu (PDFium, code natif exposé à des PDF reçus) sous un compte dédié, sans aucun droit sur l'API : ni son
 * environnement (secrets), ni sa mémoire, ni ses fichiers, ni ses signaux.
 *
 * Déroulé : contrôles (appelant = compte de l'API, arguments numériques), descripteurs hérités fermés au-delà de 0, 1
 * et 2, ménage des processus du compte du rendu laissés par un rendu précédent, puis fork.
 *  - L'enfant prend définitivement le compte du rendu (groupes vidés ; identifiants réels, effectifs et sauvegardés),
 *    vérifie qu'il ne peut plus revenir en arrière, s'interdit tout nouveau privilège, demande au noyau d'être tué si le
 *    lanceur s'arrête, s'interdit de créer des processus et d'ouvrir une connexion réseau (seccomp : un code exécuté
 *    dans PDFium ne peut ni laisser de copie de lui-même survivre au rendu, ni épier les rendus suivants, ni joindre
 *    l'API interne ou Internet), puis exécute le programme de rendu avec un environnement fixe.
 *  - Le lanceur abandonne tout privilège (identifiant réel : compte de l'API ; effectif et sauvegardé : compte du
 *    rendu), attend l'enfant et rend son code de sortie. L'API arrête un rendu en tuant le lanceur : l'enfant, qu'elle
 *    ne peut pas tuer elle-même, est alors tué par le noyau (PR_SET_PDEATHSIG).
 *
 * Tout refus : code 126 et une ligne sur la sortie d'erreur (journalisée par l'API), aucun rendu.
 * Comptes et chemins fixés à la compilation (server/Dockerfile) ; vérifié par server/eng/verify-image.sh.
 */
#define _GNU_SOURCE
#include <errno.h>
#include <grp.h>
#include <linux/audit.h>
#include <linux/filter.h>
#include <linux/seccomp.h>
#include <signal.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

#if !defined(UID_API) || !defined(UID_RENDU) || !defined(GID_RENDU) || !defined(DOTNET) || !defined(PROGRAMME)
#error "UID_API, UID_RENDU, GID_RENDU, DOTNET et PROGRAMME sont fixés à la compilation (server/Dockerfile)."
#endif

enum
{
    ARGUMENTS = 4,
    CHIFFRES_MAX = 19,
    CODE_REFUS = 126
};

/*
 * Numéros d'appels système fixés par l'ABI de chaque architecture. Les deux sont filtrés quelle que soit l'architecture
 * de compilation : sous émulation (image amd64 sur un poste arm64), le noyau voit les appels de l'hôte.
 */
enum
{
    X86_64_SOCKET = 41,
    X86_64_CLONE = 56,
    X86_64_FORK = 57,
    X86_64_VFORK = 58,
    X86_64_PRCTL = 157,
    X32_BIT = 0x40000000,
    AARCH64_SOCKET = 198,
    AARCH64_CLONE = 220,
    AARCH64_PRCTL = 167,
    IO_URING_SETUP = 425,
    CLONE3 = 435,
    DRAPEAU_FIL = 0x00010000 /* CLONE_THREAD */
};

/*
 * Filtre seccomp de l'enfant : fork, vfork et clone sans CLONE_THREAD refusés (EPERM), clone3 indisponible (ENOSYS :
 * la bibliothèque C se replie sur clone pour créer des fils d'exécution), PR_SET_PDEATHSIG refusé (l'enfant ne peut pas
 * survivre à l'arrêt du lanceur), aucun réseau (socket refusé, et io_uring qui pourrait en ouvrir ; le rendu n'utilise
 * que l'entrée, la sortie et la sortie d'erreur héritées), ABI x32 et autres architectures tuées.
 */
static int interdire_processus(void)
{
    struct sock_filter filtre[] = {
        /* 0 */ BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, arch)),
        /* 1 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AUDIT_ARCH_X86_64, 0, 8),
        /* 2 */ BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, nr)),
        /* 3 */ BPF_JUMP(BPF_JMP | BPF_JGE | BPF_K, X32_BIT, 20, 0),
        /* 4 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, X86_64_FORK, 17, 0),
        /* 5 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, X86_64_VFORK, 16, 0),
        /* 6 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, X86_64_SOCKET, 15, 0),
        /* 7 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, X86_64_CLONE, 9, 0),
        /* 8 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, X86_64_PRCTL, 10, 0),
        /* 9 */ BPF_JUMP(BPF_JMP | BPF_JA, 5, 0, 0),
        /* 10 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AUDIT_ARCH_AARCH64, 0, 13),
        /* 11 */ BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, nr)),
        /* 12 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AARCH64_SOCKET, 9, 0),
        /* 13 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AARCH64_CLONE, 3, 0),
        /* 14 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, AARCH64_PRCTL, 4, 0),
        /* 15 : communs aux deux architectures */
        BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, IO_URING_SETUP, 6, 0),
        /* 16 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, CLONE3, 6, 4),
        /* 17 : clone, drapeaux (32 bits de poids faible, petit-boutiste) */
        BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, args[0])),
        /* 18 */ BPF_JUMP(BPF_JMP | BPF_JSET | BPF_K, DRAPEAU_FIL, 2, 3),
        /* 19 : prctl, option */
        BPF_STMT(BPF_LD | BPF_W | BPF_ABS, offsetof(struct seccomp_data, args[0])),
        /* 20 */ BPF_JUMP(BPF_JMP | BPF_JEQ | BPF_K, PR_SET_PDEATHSIG, 1, 0),
        /* 21 */ BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ALLOW),
        /* 22 */ BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ERRNO | (EPERM & SECCOMP_RET_DATA)),
        /* 23 */ BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_ERRNO | (ENOSYS & SECCOMP_RET_DATA)),
        /* 24 */ BPF_STMT(BPF_RET | BPF_K, SECCOMP_RET_KILL_PROCESS),
    };
    struct sock_fprog programme = {.len = (unsigned short)(sizeof(filtre) / sizeof(filtre[0])), .filter = filtre};
    return prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &programme, 0L, 0L);
}

static int refuser(const char *etape, int erreur)
{
    dprintf(STDERR_FILENO, "lanceur-rendu-pdf : %s (%s)\n", etape, erreur != 0 ? strerror(erreur) : "refus");
    return CODE_REFUS;
}

/*
 * Ménage avant chaque rendu : tout processus du compte du rendu encore en vie (copie laissée par un rendu précédent
 * malgré seccomp, ou là où seccomp manque) est tué avant que le nouveau rendu ne commence ; il ne voit donc jamais le
 * PDF suivant. Un processus « balai » prend le compte du rendu et signale tout ce qu'il peut signaler (kill -1) : les
 * processus de ce compte seulement, jamais l'API, le web ni ce lanceur (encore root). L'API ne lance qu'un rendu à la
 * fois (ProcessusRenduPdf) : aucun rendu légitime n'est en cours.
 */
static int nettoyer(void)
{
    pid_t balai = fork();
    if (balai < 0)
    {
        return -1;
    }

    if (balai == 0)
    {
        if (setgroups(0, NULL) != 0 || setresgid(GID_RENDU, GID_RENDU, GID_RENDU) != 0 ||
            setresuid(UID_RENDU, UID_RENDU, UID_RENDU) != 0 || (kill(-1, SIGKILL) != 0 && errno != ESRCH))
        {
            _exit(1);
        }

        _exit(0);
    }

    int etat;
    while (waitpid(balai, &etat, 0) < 0)
    {
        if (errno != EINTR)
        {
            return -1;
        }
    }

    return WIFEXITED(etat) && WEXITSTATUS(etat) == 0 ? 0 : -1;
}

/* Nombre décimal sans signe, de 1 à 19 chiffres : seuls arguments transmis au programme de rendu. */
static int numerique(const char *texte)
{
    size_t longueur = strnlen(texte, CHIFFRES_MAX + 1);
    if (longueur == 0 || longueur > CHIFFRES_MAX)
    {
        return 0;
    }

    for (size_t i = 0; i < longueur; i++)
    {
        if (texte[i] < '0' || texte[i] > '9')
        {
            return 0;
        }
    }

    return 1;
}

/* Enfant : compte du rendu sans retour possible, puis programme de rendu. Ne rend la main qu'en cas d'échec. */
static int lancer_rendu(pid_t lanceur, char *argv[])
{
    if (setgroups(0, NULL) != 0)
    {
        return refuser("groupes", errno);
    }

    if (setresgid(GID_RENDU, GID_RENDU, GID_RENDU) != 0)
    {
        return refuser("groupe du rendu", errno);
    }

    if (setresuid(UID_RENDU, UID_RENDU, UID_RENDU) != 0)
    {
        return refuser("compte du rendu", errno);
    }

    uid_t ru;
    uid_t eu;
    uid_t su;
    gid_t rg;
    gid_t eg;
    gid_t sg;
    if (setresuid(0, 0, 0) == 0 || setresuid(UID_API, UID_API, UID_API) == 0 || getresuid(&ru, &eu, &su) != 0 ||
        getresgid(&rg, &eg, &sg) != 0 || ru != UID_RENDU || eu != UID_RENDU || su != UID_RENDU || rg != GID_RENDU ||
        eg != GID_RENDU || sg != GID_RENDU || getgroups(0, NULL) != 0)
    {
        return refuser("abandon des droits", 0);
    }

    if (prctl(PR_SET_NO_NEW_PRIVS, 1L, 0L, 0L, 0L) != 0)
    {
        return refuser("aucun nouveau privilège", errno);
    }

    /* Signal d'arrêt du parent remis à zéro par le changement de compte : posé après, puis lanceur vérifié vivant. */
    if (prctl(PR_SET_PDEATHSIG, SIGKILL, 0L, 0L, 0L) != 0)
    {
        return refuser("arrêt avec le lanceur", errno);
    }

    if (getppid() != lanceur)
    {
        return refuser("lanceur arrêté", 0);
    }

    /* Seccomp absent (noyau sans filtre, émulation Rosetta) : seul cas toléré, le ménage du lanceur suivant reste. */
    if (interdire_processus() != 0)
    {
        if (errno != EINVAL)
        {
            return refuser("filtre seccomp (processus, réseau)", errno);
        }

        dprintf(STDERR_FILENO, "lanceur-rendu-pdf : seccomp indisponible, création de processus et réseau permis\n");
    }

    if (chdir("/") != 0)
    {
        return refuser("dossier courant", errno);
    }

    char *environnement[] = {"DOTNET_gcServer=0", "DOTNET_EnableDiagnostics=0", NULL};
    char *arguments[] = {"dotnet", PROGRAMME, argv[1], argv[2], argv[3], argv[4], NULL};
    execve(DOTNET, arguments, environnement);
    return refuser("programme de rendu", errno);
}

int main(int argc, char *argv[])
{
    if (getuid() != UID_API)
    {
        return refuser("appelant", 0);
    }

    if (argc != 1 + ARGUMENTS)
    {
        return refuser("arguments", 0);
    }

    for (int i = 1; i <= ARGUMENTS; i++)
    {
        if (!numerique(argv[i]))
        {
            return refuser("arguments", 0);
        }
    }

    /* Seules l'entrée (PDF), la sortie (image) et la sortie d'erreur passent à l'enfant. */
    closefrom(STDERR_FILENO + 1);

    if (nettoyer() != 0)
    {
        return refuser("ménage des rendus précédents", 0);
    }

    pid_t lanceur = getpid();
    pid_t enfant = fork();
    if (enfant < 0)
    {
        return refuser("fork", errno);
    }

    if (enfant == 0)
    {
        _exit(lancer_rendu(lanceur, argv));
    }

    /*
     * Lanceur : plus aucun privilège avant toute attente, entre les deux comptes. Son identifiant réel (compte de l'API)
     * permet à l'API de le tuer ; son identifiant effectif (compte du rendu) permet au noyau de transmettre son arrêt à
     * l'enfant, car PR_SET_PDEATHSIG est soumis aux droits de signal du parent. L'enfant ne peut ni le tracer ni lire sa
     * mémoire (identifiant réel différent, processus non vidable). En cas d'échec, encore root : l'enfant est tué.
     */
    if (setgroups(0, NULL) != 0 || setresgid(GID_RENDU, GID_RENDU, GID_RENDU) != 0 ||
        setresuid(UID_API, UID_RENDU, UID_RENDU) != 0)
    {
        int erreur = errno;
        kill(enfant, SIGKILL);
        return refuser("abandon des droits du lanceur", erreur);
    }

    if (setresuid(0, 0, 0) == 0)
    {
        return refuser("abandon des droits du lanceur", 0);
    }

    /* L'entrée et la sortie ne dépendent que de l'enfant : un rendu arrêté ferme ses tubes. */
    close(STDIN_FILENO);
    close(STDOUT_FILENO);

    int etat;
    while (waitpid(enfant, &etat, 0) < 0)
    {
        if (errno != EINTR)
        {
            return refuser("attente du rendu", errno);
        }
    }

    return WIFEXITED(etat) ? WEXITSTATUS(etat) : 128 + WTERMSIG(etat);
}
