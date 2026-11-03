using GestionSite.Modules.Maintenance.Domain;
using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Planificateur;

/// <summary>
/// Destinataires d'un rappel, résolus au moment de l'envoi parmi les membres actuels du site : R/D du site
/// (<paramref name="Encadrement"/>) et personnes en charge (intervenant, ou référent d'un prestataire).
/// </summary>
internal sealed record CibleRappel(bool Encadrement, IReadOnlyCollection<Guid> EnCharge);

/// <summary>Rappel dû pour un dossier : type, clé de l'événement (une notification par destinataire et par seuil) et cible.</summary>
internal sealed record RappelDu(Signalement Dossier, TypeNotification Type, string Cle, CibleRappel Cible);

/// <summary>Rappels dus maintenant et prochain seuil à venir (réveil du planificateur), null s'il n'y en a aucun.</summary>
internal sealed record EcheancesDossier(IReadOnlyList<RappelDu> Dus, DateTimeOffset? Prochain);

/// <summary>
/// Rappels et escalades automatiques d'un dossier ouvert (US-10, S10-02 et S10-03), calculés à partir de son état et de
/// l'instant donné (horloge contrôlée) ; rien n'est stocké en dehors des notifications, dont la clé d'unicité empêche
/// tout doublon d'un même seuil, même si le passage est rejoué (T-04.11).
/// <list type="bullet">
/// <item>DEC-05 : à 75 % d'un délai (prise en charge, résolution), la personne en charge, les R/D si le dossier n'est pas
/// affecté ; à 100 %, les R/D (escalade) ; puis toutes les 24 h, les R/D et la personne en charge (choix de l'agent).
/// Délais en durées exactes : le rappel quotidien revient 24 h après, même aux changements d'heure.</item>
/// <item>Q8-A : dossier resté « À qualifier » 24 h après sa réception (ou sa réouverture), puis toutes les 24 h : R/D.</item>
/// <item>Q9-A : le jour du réexamen d'une attente à 8 h (Paris), la personne en charge ; à J+3, les R/D ; puis chaque
/// semaine, les deux.</item>
/// </list>
/// Seul le seuil le plus avancé atteint est émis : après une interruption, on ne rattrape pas les seuils intermédiaires.
/// </summary>
internal static class Rappels
{
    internal static readonly TimeSpan Jour = TimeSpan.FromDays(1);
    internal static readonly TimeOnly HeureReexamen = new(8, 0);
    internal const int JoursAvantEscaladeReexamen = 3;
    private const int JoursParSemaine = 7;

    /// <param name="dossier">Dossier, ouvert ou non (un dossier clos n'a aucun rappel).</param>
    /// <param name="actives">Interventions non clôturées du dossier (toutes du cycle en cours).</param>
    /// <param name="maintenant">Instant du passage.</param>
    internal static EcheancesDossier Calculer(Signalement dossier, IReadOnlyCollection<Intervention> actives, DateTimeOffset maintenant)
    {
        var dus = new List<RappelDu>();
        DateTimeOffset? prochain = null;
        if (!dossier.Ouvert)
        {
            return new EcheancesDossier(dus, prochain);
        }

        void Retenir(DateTimeOffset seuil) => prochain = prochain is { } p && p <= seuil ? p : seuil;

        var enCharge = actives.Select(i => i.EnCharge).Distinct().ToList();
        if (dossier.Statut == StatutDossier.AQualifier && dossier.QualifieLe is null)
        {
            Retenir(AQualifier(dossier, maintenant, dus));
        }

        if (dossier.Priorite is { } priorite && dossier.QualifieLe is { } qualifie)
        {
            if (dossier.EcheancePriseEnCharge is not null)
            {
                Retenir(Delai(
                    dossier, "pec", qualifie, Delais.PriseEnCharge(priorite), enCharge,
                    TypeNotification.PriseEnChargeProche, TypeNotification.PriseEnChargeDepassee, maintenant, dus));
            }

            // Suspendue pendant une attente (RG-07) : seuls les rappels de réexamen s'appliquent alors.
            if (dossier.EcheanceResolution is not null)
            {
                Retenir(Delai(
                    dossier, "res", qualifie + dossier.DureeAttente, Delais.Resolution(priorite), enCharge,
                    TypeNotification.ResolutionProche, TypeNotification.ResolutionDepassee, maintenant, dus));
            }
        }

        foreach (var intervention in actives.Where(i => i.Statut == StatutIntervention.EnAttente && i.ReexamenLe is not null).OrderBy(i => i.Numero))
        {
            Retenir(Reexamen(dossier, intervention, maintenant, dus));
        }

        return new EcheancesDossier(dus, prochain);
    }

    // Délai DEC-05 commencé à « debut » (temps d'attente déjà retiré). Cycle de seuils : l'échéance elle-même ; une
    // requalification ou une reprise après attente qui la déplace ouvre un nouveau cycle (escalade et rappels quotidiens
    // repartent de la nouvelle échéance), une réouverture aussi (nouvelle qualification).
    private static DateTimeOffset Delai(
        Signalement dossier, string delai, DateTimeOffset debut, TimeSpan duree, List<Guid> enCharge,
        TypeNotification proche, TypeNotification depasse, DateTimeOffset maintenant, List<RappelDu> dus)
    {
        var alerte = debut + duree * 0.75;
        var echeance = debut + duree;
        if (maintenant < alerte)
        {
            return alerte;
        }

        var cle = $"rappel:{delai}:{dossier.Id:N}:{echeance.ToUnixTimeMilliseconds()}";
        if (maintenant < echeance)
        {
            // Personne en charge, R/D tant que le dossier n'est pas affecté (DEC-05, T-04.10, T-04.14).
            dus.Add(new RappelDu(dossier, proche, $"{cle}:75", new CibleRappel(enCharge.Count == 0, enCharge)));
            return echeance;
        }

        var jours = (maintenant - echeance).Ticks / Jour.Ticks;
        dus.Add(jours == 0
            ? new RappelDu(dossier, depasse, $"{cle}:100", new CibleRappel(Encadrement: true, []))
            : new RappelDu(dossier, depasse, $"{cle}:j{jours}", new CibleRappel(Encadrement: true, enCharge)));
        return echeance + (Jour * (jours + 1));
    }

    // Q8-A : 24 h après la réception, ou après la réouverture qui l'a remis « À qualifier ».
    private static DateTimeOffset AQualifier(Signalement dossier, DateTimeOffset maintenant, List<RappelDu> dus)
    {
        var debut = dossier.RouvertLe ?? dossier.RecuLe;
        var jours = maintenant < debut ? 0 : (maintenant - debut).Ticks / Jour.Ticks;
        if (jours > 0)
        {
            dus.Add(new RappelDu(
                dossier, TypeNotification.AQualifier, $"rappel:aqualifier:{dossier.Id:N}:{debut.ToUnixTimeMilliseconds()}:{jours}",
                new CibleRappel(Encadrement: true, [])));
        }

        return debut + (Jour * (jours + 1));
    }

    // Q9-A : à 8 h (Paris) le jour du réexamen, à J+3, puis chaque semaine ; une attente nouvelle ou une date reportée
    // forment un nouveau cycle de rappels.
    private static DateTimeOffset Reexamen(Signalement dossier, Intervention intervention, DateTimeOffset maintenant, List<RappelDu> dus)
    {
        var jour = intervention.ReexamenLe!.Value;
        var premier = JourCivil.A(jour, HeureReexamen);
        if (maintenant < premier)
        {
            return premier;
        }

        var cle = $"rappel:reexamen:{intervention.Id:N}:{intervention.ReexamenFixeLe?.ToUnixTimeMilliseconds() ?? 0}:{jour:yyyyMMdd}";
        var escalade = jour.AddDays(JoursAvantEscaladeReexamen);
        if (maintenant < JourCivil.A(escalade, HeureReexamen))
        {
            // Date fixée après 8 h ce jour-là (ou pour un jour passé) : son auteur vient de la choisir, rien à lui rappeler.
            if (intervention.ReexamenFixeLe is not { } fixe || fixe < premier)
            {
                dus.Add(new RappelDu(dossier, TypeNotification.ReexamenAtteint, $"{cle}:j0", new CibleRappel(Encadrement: false, [intervention.EnCharge])));
            }

            return JourCivil.A(escalade, HeureReexamen);
        }

        var semaines = (JourCivil.Du(maintenant).DayNumber - escalade.DayNumber) / JoursParSemaine;
        if (JourCivil.A(escalade.AddDays(semaines * JoursParSemaine), HeureReexamen) > maintenant)
        {
            semaines--; // même jour de la semaine, avant 8 h
        }

        dus.Add(semaines == 0
            ? new RappelDu(dossier, TypeNotification.ReexamenDepasse, $"{cle}:j{JoursAvantEscaladeReexamen}", new CibleRappel(Encadrement: true, []))
            : new RappelDu(dossier, TypeNotification.ReexamenDepasse, $"{cle}:s{semaines}", new CibleRappel(Encadrement: true, [intervention.EnCharge])));
        return JourCivil.A(escalade.AddDays((semaines + 1) * JoursParSemaine), HeureReexamen);
    }
}
