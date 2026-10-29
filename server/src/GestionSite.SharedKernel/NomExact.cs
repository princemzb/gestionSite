namespace GestionSite.SharedKernel;

/// <summary>Valeur d'énumération reçue en texte : nom exact seulement (Enum.TryParse accepterait « 0 » ou « A, B »).</summary>
public static class NomExact
{
    public static bool Essayer<T>(string? texte, out T valeur) where T : struct, Enum
    {
        valeur = default;
        return texte is not null && Enum.GetNames<T>().Contains(texte, StringComparer.Ordinal) && Enum.TryParse(texte, out valeur);
    }
}
