using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GestionSite.Tests;

/// <summary>
/// PDF synthétiques pour les tests du rendu de plan (S21-05) : pages colorées de tailles diverses, PDF chiffrés (gestionnaire
/// standard, révision 2), PDF piégé (formulaires imbriqués). Aucune donnée réelle.
/// </summary>
internal static class PdfSynthetique
{
    private static readonly byte[] Remplissage = Convert.FromHexString("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");

    /// <summary>Une page par entrée (taille en points, couleur RVB de 0 à 1 qui la remplit, trait noir en diagonale).</summary>
    internal static byte[] Pages(params (double Largeur, double Hauteur, (double R, double V, double B) Couleur)[] pages)
    {
        var objets = new List<byte[]> { Texte("<< /Type /Catalog /Pages 2 0 R >>") };
        var enfants = string.Join(' ', pages.Select((_, i) => $"{3 + (2 * i)} 0 R"));
        objets.Add(Texte($"<< /Type /Pages /Kids [{enfants}] /Count {pages.Length} >>"));
        foreach (var (i, (largeur, hauteur, (r, v, b))) in pages.Index())
        {
            objets.Add(Texte(Inv($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {largeur} {hauteur}] /Contents {4 + (2 * i)} 0 R >>")));
            objets.Add(Flux("<<", Encoding.ASCII.GetBytes(Inv($"{r} {v} {b} rg 0 0 {largeur} {hauteur} re f 0 0 0 RG 4 w 10 10 m {largeur - 10} {hauteur - 10} l S"))));
        }

        return Assembler(objets);
    }

    /// <summary>Page blanche de 100 × 100 pt, alourdie d'un objet inutilisé de <paramref name="remplissage"/> octets.</summary>
    internal static byte[] PageBlanche(int remplissage) => Assembler(
    [
        Texte("<< /Type /Catalog /Pages 2 0 R >>"),
        Texte("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
        Texte("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>"),
        Flux("<<", Enumerable.Repeat((byte)'x', remplissage).ToArray()),
    ]);

    /// <summary>
    /// PDF chiffré (RC4 40 bits) : avec un mot de passe d'ouverture, ou sans (simples restrictions, il s'ouvre alors seul).
    /// </summary>
    internal static byte[] Chiffre(string motDePasseOuverture)
    {
        var identifiant = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var utilisateur = Complete(motDePasseOuverture);
#pragma warning disable CA5351 // MD5 et RC4 : algorithme imposé par le format PDF (révision 2), pour un fichier de test
        var proprietaire = Rc4(MD5.HashData(Complete("proprietaire"))[..5], utilisateur);
        var cle = MD5.HashData([.. utilisateur, .. proprietaire, .. BitConverter.GetBytes(-4), .. identifiant])[..5];
#pragma warning restore CA5351
        var controle = Rc4(cle, Remplissage);
        var objets = new List<byte[]>
        {
            Texte("<< /Type /Catalog /Pages 2 0 R >>"),
            Texte("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Texte("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>"),
            Texte($"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(proprietaire)}> /U <{Convert.ToHexString(controle)}> /P -4 >>"),
        };
        var id = Convert.ToHexString(identifiant);
        return Assembler(objets, $"/Encrypt 4 0 R /ID [<{id}> <{id}>] ");
    }

    /// <summary>
    /// Formulaires imbriqués : chaque niveau dessine <paramref name="copies"/> fois le précédent, soit copies^niveaux
    /// rectangles (10^10 par défaut) pour un fichier de quelques kilo-octets : rendu sans fin.
    /// </summary>
    internal static byte[] FormulairesImbriques(int niveaux = 10, int copies = 10)
    {
        var appels = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("/F Do ", copies)));
        var objets = new List<byte[]>
        {
            Texte("<< /Type /Catalog /Pages 2 0 R >>"),
            Texte("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Texte($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /XObject << /F {4 + niveaux} 0 R >> >> >>"),
            Flux("<<", appels),
            Flux("<< /Type /XObject /Subtype /Form /BBox [0 0 100 100]", "0 0 1 rg 0 0 100 100 re f"u8.ToArray()),
        };
        for (var k = 1; k < niveaux; k++)
        {
            objets.Add(Flux($"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /XObject << /F {4 + k} 0 R >> >>", appels));
        }

        return Assembler(objets);
    }

    /// <summary>En-tête PDF suivi d'octets sans structure.</summary>
    internal static byte[] Corrompu() => [.. "%PDF-1.7\n"u8, .. Enumerable.Range(0, 10_000).Select(i => (byte)(i * 7))];

    /// <summary>Objets numérotés à partir de 1, table de références et fin de fichier valides.</summary>
    internal static byte[] Assembler(IReadOnlyList<byte[]> objets, string complementTrailer = "")
    {
        using var pdf = new MemoryStream();
        pdf.Write("%PDF-1.7\n"u8);
        var positions = new List<long>();
        foreach (var (i, objet) in objets.Index())
        {
            positions.Add(pdf.Position);
            pdf.Write(Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n"));
            pdf.Write(objet);
            pdf.Write("\nendobj\n"u8);
        }

        var table = pdf.Position;
        var references = new StringBuilder(Inv($"xref\n0 {objets.Count + 1}\n0000000000 65535 f \n"));
        foreach (var position in positions)
        {
            references.Append(Inv($"{position:D10} 00000 n \n"));
        }

        references.Append(Inv($"trailer\n<< /Size {objets.Count + 1} /Root 1 0 R {complementTrailer}>>\nstartxref\n{table}\n%%EOF\n"));
        pdf.Write(Encoding.ASCII.GetBytes(references.ToString()));
        return pdf.ToArray();
    }

    internal static byte[] Texte(string objet) => Encoding.ASCII.GetBytes(objet);

    private static byte[] Flux(string debutDictionnaire, byte[] contenu) =>
        [.. Encoding.ASCII.GetBytes($"{debutDictionnaire} /Length {contenu.Length} >>\nstream\n"), .. contenu, .. "\nendstream"u8];

    private static string Inv(FormattableString texte) => texte.ToString(CultureInfo.InvariantCulture);

    private static byte[] Complete(string motDePasse) => [.. Encoding.ASCII.GetBytes(motDePasse).Concat(Remplissage).Take(32)];

    private static byte[] Rc4(byte[] cle, byte[] donnees)
    {
        var s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + cle[i % cle.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var sortie = new byte[donnees.Length];
        for (int n = 0, i = 0, j = 0; n < donnees.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            sortie[n] = (byte)(donnees[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return sortie;
    }
}
