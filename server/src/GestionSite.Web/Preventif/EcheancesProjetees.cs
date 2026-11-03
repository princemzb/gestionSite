using GestionSite.Web.Session;

namespace GestionSite.Web.Preventif;

/// <summary>Échéance placée au calendrier : la prochaine (état calculé par l'API) ou une suivante, prévue.</summary>
internal sealed record EcheanceCalendrier(PlanApi Plan, DateOnly Date, string Etat, bool Prevue);

/// <summary>
/// Échéances d'un plan sur la période affichée : la prochaine telle que l'API la calcule, puis les suivantes prévues par
/// la périodicité, comptées comme l'API (Echeancier) : en mode fixe depuis la première échéance (sans dérive de fin de
/// mois), en mode glissant depuis la prochaine. Aucune date prévue avant aujourd'hui : seule l'échéance courante peut être
/// passée (en retard). Une échéance glissante suivra la réalisation : ses dates prévues sont indicatives. Plan suspendu ou
/// ponctuel réalisé : rien.
/// </summary>
internal static class EcheancesProjetees
{
    private const int PeriodesMax = 6000; // plus de cent ans de semaines : borne de sécurité

    internal static Dictionary<DateOnly, List<EcheanceCalendrier>> Projeter(IEnumerable<PlanApi> plans, DateOnly debut, DateOnly fin, DateOnly aujourdhui)
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

            var origine = plan.Mode == "Fixe" ? plan.PremiereEcheance : prochaine.Echeance;
            var depuis = new[] { debut, aujourdhui, prochaine.Echeance.AddDays(1) }.Max();
            for (var k = PremierRang(origine, periode, depuis); k <= PeriodesMax; k++)
            {
                var date = Apres(origine, periode, k);
                if (date > fin)
                {
                    break;
                }

                if (date >= depuis)
                {
                    Ajouter(parJour, new EcheanceCalendrier(plan, date, "AVenir", Prevue: true), debut, fin);
                }
            }
        }

        return parJour;
    }

    // Rang à partir duquel chercher : une période dure au plus 366 jours par an, 31 par mois, donc origine + rang périodes
    // reste avant « depuis » ; la boucle n'avance que sur les quelques périodes restantes.
    private static int PremierRang(DateOnly origine, PeriodeApi periode, DateOnly depuis)
    {
        var joursMax = periode.Valeur * (periode.Unite switch { "Ans" => 366L, "Mois" => 31L, _ => 7L });
        return (int)Math.Clamp((depuis.DayNumber - origine.DayNumber) / joursMax, 0, PeriodesMax);
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
