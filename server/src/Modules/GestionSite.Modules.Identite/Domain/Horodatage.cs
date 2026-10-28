namespace GestionSite.Modules.Identite.Domain;

/// <summary>Instants stockés en « timestamp with time zone » : UTC, à la microseconde, jamais ±infinity.</summary>
internal static class Horodatage
{
    internal static DateTimeOffset Valider(DateTimeOffset instant, string paramName)
    {
        // PostgreSQL : UTC seulement, à la microseconde (le reste serait perdu au stockage).
        var utc = instant.ToUniversalTime();
        utc = utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));

        // Valeur par défaut ou bornes extrêmes : Npgsql écrirait « -infinity » / « infinity ».
        // Minimum testé après arrondi (MinValue + quelques ticks y retombe) ; maximum avant.
        if (utc.UtcDateTime == DateTime.MinValue || instant.UtcDateTime == DateTime.MaxValue)
        {
            throw new ArgumentException("La date est obligatoire et doit être une date réelle.", paramName);
        }

        return utc;
    }
}
