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

    /// <summary>Date à l'heure de Paris (« dd/MM/yyyy » ou avec l'heure), ou en UTC signalé comme tel.</summary>
    internal static string Date(DateTimeOffset date, bool avecHeure = false)
    {
        var format = avecHeure ? "dd/MM/yyyy · HH:mm" : "dd/MM/yyyy";
        return Paris is null
            ? date.ToUniversalTime().ToString($"{format} 'UTC'", Francais)
            : TimeZoneInfo.ConvertTime(date, Paris).ToString(format, Francais);
    }
}
