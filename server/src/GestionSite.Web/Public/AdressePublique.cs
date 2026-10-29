namespace GestionSite.Web.Public;

/// <summary>Racine des liens donnés à d'autres personnes (activation, formulaire public) : adresse publique configurée, sinon celle de la requête.</summary>
internal static class AdressePublique
{
    internal static string Racine(WebOptions options, string baseRequete) =>
        (options.UrlPublique is { Length: > 0 } publique ? publique : baseRequete).TrimEnd('/') + "/";
}
