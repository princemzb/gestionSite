// Sonde d'isolation du rendu PDF (SOC-05), pour server/eng/verify-image.sh seulement : jamais dans l'image livrée.
// L'image d'essai la met à la place du programme de rendu. Exécutée par le vrai lanceur, elle a exactement les droits du
// processus de rendu, celui où une faille de PDFium ferait exécuter du code, et tente d'atteindre l'API et l'application web.
// Arguments (numériques, comme ceux du rendu) : PID de l'API, PID de l'application web, inutilisé, 0 pour rester en vie
// après les essais (arrêt avec le lanceur). Sortie : noms des variables d'environnement reçues, compte, puis une ligne
// par essai, « ACCES <essai> » ou « REFUS <essai> <raison> ».
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

var api = int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
var web = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);

var etat = File.ReadAllLines("/proc/self/status").Select(l => l.Split(':', 2)).ToDictionary(c => c[0], c => c[1].Trim());
Console.WriteLine($"ENVIRONNEMENT {string.Join(' ', Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal))}");
Console.WriteLine($"COMPTE uid={etat["Uid"]} gid={etat["Gid"]} groupes=[{etat["Groups"]}] capacites={etat["CapEff"]}/{etat["CapPrm"]} nnp={etat["NoNewPrivs"]}");

// Lanceur (parent) : ni environnement, ni mémoire, ni descripteurs ; le signaler reste possible et sans effet utile
// (son arrêt arrête l'enfant).
foreach (var (nom, pid) in new[] { ("api", api), ("web", web), ("pid1", 1), ("lanceur", getppid()) })
{
    Essayer($"environ-{nom}", () => File.ReadAllBytes($"/proc/{pid}/environ"));
    Essayer($"memoire-{nom}", () => File.OpenRead($"/proc/{pid}/mem").Dispose());
    Essayer($"descripteurs-{nom}", () => Directory.GetFileSystemEntries($"/proc/{pid}/fd"));
    if (nom != "lanceur")
    {
        Essayer($"signal-{nom}", () => Signal(pid));
    }
}

Essayer("config-api", () => File.ReadAllBytes("/app/api/appsettings.json"));
Essayer("config-web", () => File.ReadAllBytes("/app/web/appsettings.json"));
Essayer("dossier-api", () => Directory.GetFileSystemEntries("/app/api"));
Essayer("dossier-web", () => Directory.GetFileSystemEntries("/app/web"));
Essayer("diagnostic-api", () => Diagnostic(api));

// Nouveau processus : une copie de la sonde qui survivrait au rendu pourrait épier les rendus suivants.
Essayer("processus", () => Process.Start("/bin/true")!.WaitForExit());

// Réseau (API interne, Internet) : la seule création d'une prise TCP suffit à l'ouvrir ; refusée par seccomp (SOC-05).
Essayer("reseau", () => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp).Dispose());

// Signal d'arrêt avec le lanceur : le désarmer permettrait de survivre à l'arrêt du rendu par l'API.
Essayer("pdeathsig", () => Verifier(prctl(1 /* PR_SET_PDEATHSIG */, 0, 0, 0, 0)));

if (args[3] == "0")
{
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
}

return 0;

static void Essayer(string essai, Action action)
{
    try
    {
        action();
        Console.WriteLine($"ACCES {essai}");
    }
    catch (Exception refus) when (refus is IOException or UnauthorizedAccessException or SocketException or Win32Exception)
    {
        Console.WriteLine($"REFUS {essai} {refus.GetType().Name}{(refus is SocketException prise ? $" {prise.SocketErrorCode}" : string.Empty)}");
    }
}

static void Signal(int pid) => Verifier(kill(pid, 0));

static void Verifier(int resultat)
{
    if (resultat != 0)
    {
        throw new IOException(Marshal.GetLastPInvokeErrorMessage());
    }
}

// Canal de diagnostic .NET de l'API (prise Unix dans /tmp) : y accéder permettrait de lui faire charger du code.
static void Diagnostic(int pid)
{
    var prises = Directory.GetFiles("/tmp", $"dotnet-diagnostic-{pid}-*-socket");
    if (prises.Length == 0)
    {
        throw new IOException("aucune prise de diagnostic");
    }

    using var prise = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    prise.Connect(new UnixDomainSocketEndPoint(prises[0]));
}

[DllImport("libc", SetLastError = true)]
static extern int kill(int pid, int signal);

[DllImport("libc")]
static extern int getppid();

[DllImport("libc", SetLastError = true)]
static extern int prctl(int option, nuint a, nuint b, nuint c, nuint d);
