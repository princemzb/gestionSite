namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Composant suivi par le score : zone actuelle, période d'activité (créé, retiré).</summary>
internal sealed record ComposantSuivi(Guid Id, Guid ZoneId, DateTimeOffset CreeLe, DateTimeOffset? RetireLe)
{
    internal bool ActifLe(DateTimeOffset le) => CreeLe <= le && (RetireLe is null || RetireLe > le);
}

/// <summary>Note d'une évaluation datée, précision complète ; « Critique » constaté (au moins une unité d'un lot).</summary>
internal sealed record NoteDatee(Guid EvaluationId, Guid ComposantId, DateTimeOffset Le, decimal Note, bool Critique);

/// <summary>État d'une zone à une date : note (null sans composant évalué), couverture, fraîcheur, points critiques.</summary>
internal sealed record EtatZone(Guid ZoneId, decimal? Note, int Actifs, int Evalues, int Anciennes, int Critiques, DateTimeOffset? PlusAncienne);

/// <summary>État du site à une date : note, couverture, part de la note reposant sur des évaluations anciennes.</summary>
internal sealed record EtatSite(decimal? Note, int Actifs, int Evalues, decimal PartAncienne, int Critiques, IReadOnlyList<EtatZone> Zones);

/// <summary>Point mensuel de l'historique : note du site à la fin du mois (ou maintenant pour le mois en cours).</summary>
internal sealed record PointHistorique(DateOnly Mois, decimal? Note);

/// <summary>
/// Score du site (DEC-01, décisions US-11 du 29/10), recalculé à toute date depuis les évaluations datées : note d'une zone
/// = moyenne des dernières notes de ses composants actifs évalués (un lot compte pour un) ; note du site = moyenne des
/// zones évaluées, Parties communes comprises. Un composant non évalué réduit la couverture sans modifier la note ; une
/// évaluation de plus de 6 mois est « ancienne » et reste comptée. Précision complète au calcul (RG-12).
/// </summary>
internal static class CalculScore
{
    internal const int MoisAncienne = 6;
    internal const int MoisHistorique = 12;

    // Fuseau des mois de l'historique et de l'affichage (Europe/Paris), UTC à défaut sur l'hôte.
    private static readonly TimeZoneInfo Fuseau =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var paris) ? paris : TimeZoneInfo.Utc;

    internal static bool Ancienne(DateTimeOffset evalueLe, DateTimeOffset le) => evalueLe < le.AddMonths(-MoisAncienne);

    internal static EtatSite Calculer(
        IReadOnlyCollection<Guid> zones, IReadOnlyList<ComposantSuivi> composants, IReadOnlyList<NoteDatee> notes, DateTimeOffset le)
    {
        // Dernière évaluation de chaque composant à la date (identifiant v7 pour départager deux évaluations simultanées).
        var dernieres = notes.Where(n => n.Le <= le).GroupBy(n => n.ComposantId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Le).ThenBy(n => n.EvaluationId).Last());
        var actifs = composants.Where(c => c.ActifLe(le)).ToList();
        var etats = zones.Select(zone =>
        {
            var deZone = actifs.Where(c => c.ZoneId == zone).ToList();
            var evaluees = deZone.Where(c => dernieres.ContainsKey(c.Id)).Select(c => dernieres[c.Id]).ToList();
            return new EtatZone(
                zone, evaluees.Count == 0 ? null : evaluees.Average(n => n.Note), deZone.Count, evaluees.Count,
                evaluees.Count(n => Ancienne(n.Le, le)), evaluees.Count(n => n.Critique), evaluees.Count == 0 ? null : evaluees.Min(n => n.Le));
        }).ToList();

        // Part ancienne : poids, dans la note du site, des composants dont la dernière évaluation est ancienne (chaque zone
        // évaluée pèse 1/n zones, réparti à parts égales entre ses composants évalués).
        var notees = etats.Where(z => z.Note is not null).ToList();
        var partAncienne = notees.Count == 0 ? 0m : notees.Sum(z => (decimal)z.Anciennes / z.Evalues) / notees.Count;
        return new EtatSite(
            notees.Count == 0 ? null : notees.Average(z => z.Note!.Value), etats.Sum(z => z.Actifs), etats.Sum(z => z.Evalues), partAncienne,
            etats.Sum(z => z.Critiques), etats);
    }

    /// <summary>Note du site à la fin de chacun des 12 derniers mois (le plus ancien d'abord), le mois en cours arrêté à maintenant.</summary>
    internal static IReadOnlyList<PointHistorique> Historique(
        IReadOnlyCollection<Guid> zones, IReadOnlyList<ComposantSuivi> composants, IReadOnlyList<NoteDatee> notes, DateTimeOffset maintenant)
    {
        var local = TimeZoneInfo.ConvertTime(maintenant, Fuseau);
        var moisCourant = new DateOnly(local.Year, local.Month, 1);
        return [.. Enumerable.Range(0, MoisHistorique).Select(i => moisCourant.AddMonths(i - MoisHistorique + 1)).Select(mois =>
        {
            var fin = mois == moisCourant ? maintenant : DebutDuMois(mois.AddMonths(1)).AddTicks(-1);
            return new PointHistorique(mois, Calculer(zones, composants, notes, fin).Note);
        })];
    }

    // Minuit local du premier jour du mois, converti en instant (heure d'été comprise).
    private static DateTimeOffset DebutDuMois(DateOnly mois)
    {
        var minuit = mois.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(minuit, Fuseau.GetUtcOffset(minuit));
    }
}
