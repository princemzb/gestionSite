using System.Globalization;

namespace GestionSite.Web.Session;

/// <summary>Règles d'affichage partagées par les écrans d'un site (dépôts reçus, inventaire).</summary>
internal static class Perimetre
{
    // Repli sur UTC (fuseau absent de l'hôte) affiché comme tel : jamais une heure locale fausse.
    private static readonly TimeZoneInfo? Paris =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var fuseau) ? fuseau : null;

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Sites où l'appelant a un rôle interne : l'administrateur seul n'y a aucun accès implicite (DEC-04).</summary>
    internal static IReadOnlyList<SiteApi> SitesInternes(OrganisationApi? organisation) => organisation is null
        ? []
        : [.. organisation.Sites.Where(s => organisation.Roles.Any(r => r.SiteId == s.Id && r.Role != "Administrateur"))];

    /// <summary>Vrai si l'appelant a l'un de ces rôles sur ce site (affichage seulement : l'API reste juge des droits).</summary>
    internal static bool ARole(OrganisationApi? organisation, Guid? siteId, params string[] roles) =>
        organisation?.Roles.Any(r => r.SiteId == siteId && roles.Contains(r.Role, StringComparer.Ordinal)) == true;

    /// <summary>Jour calendaire à Paris (repli : UTC), pour proposer une date dans un formulaire.</summary>
    internal static DateOnly Jour(DateTimeOffset maintenant) =>
        DateOnly.FromDateTime(Paris is null ? maintenant.UtcDateTime : TimeZoneInfo.ConvertTime(maintenant, Paris).DateTime);

    /// <summary>Date à l'heure de Paris (« dd/MM/yyyy » ou avec l'heure), ou en UTC signalé comme tel.</summary>
    internal static string Date(DateTimeOffset date, bool avecHeure = false)
    {
        var format = avecHeure ? "dd/MM/yyyy · HH:mm" : "dd/MM/yyyy";
        return Paris is null
            ? date.ToUniversalTime().ToString($"{format} 'UTC'", Francais)
            : TimeZoneInfo.ConvertTime(date, Paris).ToString(format, Francais);
    }
}
