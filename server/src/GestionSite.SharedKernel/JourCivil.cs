namespace GestionSite.SharedKernel;

/// <summary>
/// Calendrier métier : celui de Paris, comme l'affichage web (mois de l'historique du score, échéances du préventif) ;
/// UTC à défaut de base des fuseaux sur l'hôte.
/// </summary>
public static class JourCivil
{
    public static readonly TimeZoneInfo Fuseau =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var paris) ? paris : TimeZoneInfo.Utc;

    /// <summary>Jour calendaire de l'instant à Paris.</summary>
    public static DateOnly Du(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Fuseau).DateTime);
}
