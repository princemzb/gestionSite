using GestionSite.Web.Session;

namespace GestionSite.Web.Preventif;

/// <summary>Échéance placée au calendrier : la prochaine (état calculé par l'API) ou une suivante, prévue.</summary>
internal sealed record EcheanceCalendrier(PlanApi Plan, DateOnly Date, string Etat, bool Prevue);

/// <summary>
/// Échéances d'un plan sur la période affichée : la prochaine telle que l'API la calcule, puis les suivantes prévues par
/// la périodicité (prochaine + k périodes, sans dérive de fin de mois). Une échéance glissante suivra la réalisation :
/// ses dates prévues sont indicatives. Plan suspendu ou ponctuel réalisé : rien.
/// </summary>
internal static class EcheancesProjetees
{
    private const int PeriodesMax = 6000; // plus de cent ans de semaines : borne de sécurité

    internal static Dictionary<DateOnly, List<EcheanceCalendrier>> Projeter(IEnumerable<PlanApi> plans, DateOnly debut, DateOnly fin)
    {
        var parJour = new Dictionary<DateOnly, List<EcheanceCalendrier>>();
        foreach (var plan in plans)
        {
            if (plan.Suspendu || plan.Prochaine is not { } prochaine)
            {
                continue;
            }

            Ajouter(parJour, new EcheanceCalendrier(plan, prochaine.Echeance, prochaine.Etat, Prevue: false), debut, fin);
            if (plan.Periode is not { Valeur: > 0 } periode)
            {
                continue;
            }

            for (var k = 1; k <= PeriodesMax; k++)
            {
                var date = Apres(prochaine.Echeance, periode, k);
                if (date > fin)
                {
                    break;
                }

                Ajouter(parJour, new EcheanceCalendrier(plan, date, "AVenir", Prevue: true), debut, fin);
            }
        }

        return parJour;
    }

    internal static DateOnly Apres(DateOnly date, PeriodeApi periode, int fois)
    {
        // Au-delà du calendrier : date maximale (la projection s'arrête à la fin de la période affichée).
        var n = (long)periode.Valeur * fois;
        return periode.Unite switch
        {
            "Ans" => n > DateOnly.MaxValue.Year - date.Year ? DateOnly.MaxValue : date.AddYears((int)n),
            "Mois" => n > (DateOnly.MaxValue.Year - date.Year) * 12L ? DateOnly.MaxValue : date.AddMonths((int)n),
            _ => n * 7 > DateOnly.MaxValue.DayNumber - date.DayNumber ? DateOnly.MaxValue : date.AddDays((int)(n * 7)),
        };
    }

    private static void Ajouter(Dictionary<DateOnly, List<EcheanceCalendrier>> parJour, EcheanceCalendrier echeance, DateOnly debut, DateOnly fin)
    {
        if (echeance.Date < debut || echeance.Date > fin)
        {
            return;
        }

        if (!parJour.TryGetValue(echeance.Date, out var liste))
        {
            parJour[echeance.Date] = liste = [];
        }

        liste.Add(echeance);
    }
}
