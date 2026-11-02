/*
 * Lanceur du rendu PDF isolé (SOC-05), seul programme setuid de l'image (propriétaire root, mode 4750, groupe de l'API).
 *
 * L'API l'exécute avec les quatre arguments numériques du rendu (page, côté, surface, mémoire). Il lance le processus de
 * rendu (PDFium, code natif exposé à des PDF reçus) sous un compte dédié, sans aucun droit sur l'API : ni son
 * environnement (secrets), ni sa mémoire, ni ses fichiers, ni ses signaux.
 *
 * Déroulé : contrôles (appelant = compte de l'API, arguments numériques), descripteurs hérités fermés au-delà de 0, 1
 * et 2, puis fork.
 *  - L'enfant prend définitivement le compte du rendu (groupes vidés ; identifiants réels, effectifs et sauvegardés),
 *    vérifie qu'il ne peut plus revenir en arrière, s'interdit tout nouveau privilège, demande au noyau d'être tué si le
 *    lanceur s'arrête, puis exécute le programme de rendu avec un environnement fixe.
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
#include <signal.h>
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

static int refuser(const char *etape, int erreur)
{
    dprintf(STDERR_FILENO, "lanceur-rendu-pdf : %s (%s)\n", etape, erreur != 0 ? strerror(erreur) : "refus");
    return CODE_REFUS;
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
