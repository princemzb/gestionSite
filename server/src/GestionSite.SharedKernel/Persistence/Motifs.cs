namespace GestionSite.SharedKernel.Persistence;

/// <summary>Motifs ILIKE (caractère d'échappement « \ ») : les jokers % et _ d'une saisie restent littéraux.</summary>
public static class Motifs
{
    public const string Echappement = @"\";

    public static string Exact(string texte)
    {
        ArgumentNullException.ThrowIfNull(texte);
        return texte.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
    }

    public static string Contenant(string texte) => $"%{Exact(texte)}%";
}
