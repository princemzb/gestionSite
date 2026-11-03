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

    /// <summary>
    /// Instant d'une heure de Paris un jour donné (rappel « à 8 h », US-10), décalage d'été ou d'hiver de ce jour-là. Une
    /// heure sautée au passage à l'heure d'été (entre 2 h et 3 h) est avancée d'une heure ; une heure répétée en octobre
    /// prend sa première occurrence (heure d'été).
    /// </summary>
    public static DateTimeOffset A(DateOnly jour, TimeOnly heure)
    {
        var local = jour.ToDateTime(heure, DateTimeKind.Unspecified);
        if (Fuseau.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var decalage = Fuseau.IsAmbiguousTime(local) ? Fuseau.GetAmbiguousTimeOffsets(local).Max() : Fuseau.GetUtcOffset(local);
        return new DateTimeOffset(local, decalage).ToUniversalTime();
    }
}
