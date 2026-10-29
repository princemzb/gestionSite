namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Composant suivi par le score : zone actuelle, période d'activité (créé, retiré).</summary>
internal sealed record ComposantSuivi(Guid Id, Guid ZoneId, DateTimeOffset CreeLe, DateTimeOffset? RetireLe)
{
    internal bool ActifLe(DateTimeOffset le) => CreeLe <= le && (RetireLe is null || RetireLe > le);
}

/// <summary>Note exacte d'une évaluation datée ; « Critique » constaté (au moins une unité d'un lot).</summary>
internal sealed record NoteDatee(Guid EvaluationId, Guid ComposantId, DateTimeOffset Le, Rationnel Note, bool Critique);

/// <summary>État d'une zone à une date : note (null sans composant évalué), couverture, fraîcheur, points critiques.</summary>
internal sealed record EtatZone(
    Guid ZoneId, Rationnel? Note, int Actifs, int Evalues, int Anciennes, IReadOnlyList<Guid> Critiques, DateTimeOffset? PlusAncienne);

/// <summary>État du site à une date : note, couverture, part de la note reposant sur des évaluations anciennes.</summary>
internal sealed record EtatSite(Rationnel? Note, int Actifs, int Evalues, Rationnel PartAncienne, IReadOnlyList<EtatZone> Zones)
{
    /// <summary>Composants actifs dont la dernière évaluation constate « Critique » : un danger ne disparaît pas dans la moyenne.</summary>
    internal IReadOnlyList<Guid> Critiques => [.. Zones.SelectMany(z => z.Critiques)];
}

/// <summary>Point mensuel de l'historique : état du site et des zones à la fin du mois (ou maintenant pour le mois en cours).</summary>
internal sealed record PointHistorique(DateOnly Mois, EtatSite Etat);

/// <summary>
/// Score du site (DEC-01, décisions US-11 du 29/10), recalculé à toute date depuis les évaluations datées : note d'une zone
/// = moyenne des dernières notes de ses composants actifs évalués (un lot compte pour un) ; note du site = moyenne des
/// zones évaluées, Parties communes comprises. Un composant non évalué réduit la couverture sans modifier la note ; une
/// évaluation de plus de 6 mois est « ancienne » et reste comptée. Fractions exactes (RG-12) ; composant pris dans sa zone
/// actuelle à toute date.
/// </summary>
internal sealed class CalculScore
{
    internal const int MoisAncienne = 6;
    internal const int MoisHistorique = 12;

    // Fuseau des mois de l'historique (Europe/Paris, comme l'affichage web), UTC à défaut de base des fuseaux sur l'hôte.
    private static readonly TimeZoneInfo Fuseau =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Paris", out var paris) ? paris : TimeZoneInfo.Utc;

    private readonly IReadOnlyCollection<Guid> _zones;
    private readonly ILookup<Guid, ComposantSuivi> _composantsParZone;
    private readonly Dictionary<Guid, NoteDatee[]> _notesParComposant;

    // Préparation unique : notes triées par composant (date, puis identifiant v7 pour deux évaluations simultanées).
    internal CalculScore(IReadOnlyCollection<Guid> zones, IEnumerable<ComposantSuivi> composants, IEnumerable<NoteDatee> notes)
    {
        _zones = zones;
        _composantsParZone = composants.ToLookup(c => c.ZoneId);
        _notesParComposant = notes.GroupBy(n => n.ComposantId)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Le).ThenBy(n => n.EvaluationId).ToArray());
    }

    /// <summary>Plus de 6 mois avant la date, jugé au calendrier de Paris comme les mois de l'historique.</summary>
    internal static bool Ancienne(DateTimeOffset evalueLe, DateTimeOffset le) =>
        TimeZoneInfo.ConvertTime(evalueLe, Fuseau).DateTime < TimeZoneInfo.ConvertTime(le, Fuseau).DateTime.AddMonths(-MoisAncienne);

    /// <summary>Premier instant de la fenêtre de l'historique : minuit (Paris) du premier des 12 mois.</summary>
    internal static DateTimeOffset DebutHistorique(DateTimeOffset maintenant) => DebutDuMois(PremierMois(maintenant));

    internal EtatSite Calculer(DateTimeOffset le)
    {
        var etats = _zones.Select(zone =>
        {
            var actifs = _composantsParZone[zone].Where(c => c.ActifLe(le)).ToList();
            var evaluees = actifs.Select(c => Derniere(c.Id, le)).OfType<NoteDatee>().ToList();
            return new EtatZone(
                zone, evaluees.Count == 0 ? null : Rationnel.Moyenne([.. evaluees.Select(n => n.Note)]), actifs.Count, evaluees.Count,
                evaluees.Count(n => Ancienne(n.Le, le)), [.. evaluees.Where(n => n.Critique).Select(n => n.ComposantId)],
                evaluees.Count == 0 ? null : evaluees.Min(n => n.Le));
        }).ToList();

        // Part ancienne : poids, dans la note du site, des composants dont la dernière évaluation est ancienne (chaque zone
        // notée pèse 1/n, réparti à parts égales entre ses composants évalués).
        var notees = etats.Where(z => z.Note is not null).ToList();
        var partAncienne = notees.Count == 0
            ? Rationnel.Entier(0)
            : Rationnel.Moyenne([.. notees.Select(z => Rationnel.Fraction(z.Anciennes, z.Evalues))]);
        return new EtatSite(
            notees.Count == 0 ? null : Rationnel.Moyenne([.. notees.Select(z => z.Note!.Value)]), etats.Sum(z => z.Actifs), etats.Sum(z => z.Evalues),
            partAncienne, etats);
    }

    /// <summary>État à la fin de chacun des 12 derniers mois (le plus ancien d'abord) ; le dernier point est l'état actuel.</summary>
    internal IReadOnlyList<PointHistorique> Historique(DateTimeOffset maintenant)
    {
        var premier = PremierMois(maintenant);
        return [.. Enumerable.Range(0, MoisHistorique).Select(i => premier.AddMonths(i)).Select(mois =>
        {
            var suivant = DebutDuMois(mois.AddMonths(1));
            return new PointHistorique(mois, Calculer(suivant > maintenant ? maintenant : suivant.AddTicks(-1)));
        })];
    }

    // Dernière évaluation du composant à la date, ou null : recherche dichotomique dans ses notes triées par date.
    private NoteDatee? Derniere(Guid composant, DateTimeOffset le)
    {
        if (!_notesParComposant.TryGetValue(composant, out var notes))
        {
            return null;
        }

        var (bas, haut) = (0, notes.Length - 1);
        NoteDatee? trouvee = null;
        while (bas <= haut)
        {
            var milieu = bas + ((haut - bas) / 2);
            if (notes[milieu].Le <= le)
            {
                trouvee = notes[milieu];
                bas = milieu + 1;
            }
            else
            {
                haut = milieu - 1;
            }
        }

        return trouvee;
    }

    private static DateOnly PremierMois(DateTimeOffset maintenant)
    {
        var local = TimeZoneInfo.ConvertTime(maintenant, Fuseau);
        return new DateOnly(local.Year, local.Month, 1).AddMonths(1 - MoisHistorique);
    }

    // Minuit local du premier jour du mois, converti en instant UTC (heure d'été comprise ; Npgsql n'écrit que l'UTC).
    private static DateTimeOffset DebutDuMois(DateOnly mois)
    {
        var minuit = mois.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(minuit, Fuseau.GetUtcOffset(minuit)).ToUniversalTime();
    }
}
