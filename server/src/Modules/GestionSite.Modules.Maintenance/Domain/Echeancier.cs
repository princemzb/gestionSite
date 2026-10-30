namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Statut enregistré d'une occurrence matérialisée (à l'affectation, à la réalisation ou au saut).</summary>
internal enum StatutOccurrence
{
    Affectee = 1,
    Realisee = 2,
    RealiseeAvecReserves = 3,
    Sautee = 4,
}

/// <summary>Position calculée d'une échéance par rapport à aujourd'hui (aucune tolérance après l'échéance).</summary>
internal enum EtatEcheance
{
    AVenir = 1,
    AFaire = 2,
    EnRetard = 3,
}

/// <summary>Occurrence connue d'un plan : son échéance, son statut et, résolue, la date réelle de réalisation ou du saut.</summary>
internal sealed record OccurrenceConnue(DateOnly Echeance, StatutOccurrence Statut, DateOnly? Le)
{
    internal bool Resolue => Statut != StatutOccurrence.Affectee;
}

/// <summary>
/// Échéances d'un plan, calculées à la lecture (US-12, S12-02 ; aucune tâche planifiée). Une occurrence affectée et non
/// résolue reste l'échéance courante, inchangée même si le calendrier change ; sinon la prochaine se déduit de la
/// dernière résolue : en mode glissant, sa date réelle (réalisation ou saut) + la période, même faite tôt dans la
/// fenêtre ; en mode fixe, la date suivante du calendrier compté depuis la première échéance, même après un retard.
/// </summary>
internal static class Echeancier
{
    internal const int FenetreMin = 7;
    internal const int FenetreMax = 30;

    /// <summary>Échéance courante, ou null pour un plan ponctuel déjà résolu.</summary>
    internal static DateOnly? Prochaine(CalendrierPlan calendrier, IReadOnlyCollection<OccurrenceConnue> occurrences)
    {
        if (occurrences.Where(o => !o.Resolue).OrderBy(o => o.Echeance).FirstOrDefault() is { } affectee)
        {
            return affectee.Echeance;
        }

        var derniere = occurrences.Where(o => o.Resolue).MaxBy(o => o.Echeance);
        if (derniere is null)
        {
            return calendrier.PremiereEcheance;
        }

        if (calendrier.Periode is not { } periode)
        {
            return null; // ponctuel : fait ou sauté une fois pour toutes
        }

        if (calendrier.Mode == ModeEcheance.Fixe)
        {
            // Première date du calendrier après la dernière échéance résolue (bornée : au plus 120 ans de semaines).
            for (var fois = 0; fois < 10_000; fois++)
            {
                var date = periode.Apres(calendrier.PremiereEcheance, fois);
                if (date > derniere.Echeance)
                {
                    return date;
                }
            }

            return null;
        }

        // Glissant : jamais avant le lendemain de la dernière échéance (une réalisation saisie très tôt ne revient pas en arrière).
        var suivante = periode.Apres(derniere.Le ?? derniere.Echeance);
        return suivante > derniere.Echeance ? suivante : derniere.Echeance.AddDays(1);
    }

    /// <summary>
    /// Fenêtre « À faire », en jours avant l'échéance : 10 % de la période qui s'achève à l'échéance (arrondi au plus
    /// proche), entre 7 et 30 jours ; 30 jours pour un plan ponctuel.
    /// </summary>
    internal static int Fenetre(Periode? periode, DateOnly echeance)
    {
        if (periode is null)
        {
            return FenetreMax;
        }

        var jours = echeance.DayNumber - periode.Avant(echeance).DayNumber;
        return Math.Clamp((int)Math.Round(jours / 10m, MidpointRounding.AwayFromZero), FenetreMin, FenetreMax);
    }

    /// <summary>À venir avant la fenêtre, à faire de son ouverture jusqu'au jour de l'échéance compris, en retard ensuite.</summary>
    internal static EtatEcheance Etat(DateOnly echeance, int fenetre, DateOnly aujourdhui) =>
        aujourdhui > echeance ? EtatEcheance.EnRetard : aujourdhui >= echeance.AddDays(-fenetre) ? EtatEcheance.AFaire : EtatEcheance.AVenir;
}
