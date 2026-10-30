namespace GestionSite.SharedKernel;

/// <summary>
/// Jour calendaire des échéances (préventif, US-12) : celui de Paris, comme l'affichage web et l'historique du score ;
/// UTC à défaut de base des fuseaux sur l'hôte.
/// </summary>
public static class JourCivil
{
    private static readonly TimeZoneInfo Fuseau =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var paris) ? paris : TimeZoneInfo.Utc;

    public static DateOnly Du(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Fuseau).DateTime);
}
