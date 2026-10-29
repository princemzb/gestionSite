namespace GestionSite.Modules.Patrimoine.Persistence;

/// <summary>Motifs ILIKE (caractère d'échappement « \ ») : les jokers % et _ d'une saisie restent littéraux.</summary>
internal static class Motifs
{
    internal const string Echappement = @"\";

    internal static string Exact(string texte) => texte.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    internal static string Contenant(string texte) => $"%{Exact(texte)}%";
}
