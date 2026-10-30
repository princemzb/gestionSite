using GestionSite.SharedKernel;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Nature d'un plan préventif (US-12) : inspection avec la grille, vérification par un tiers, ou entretien.</summary>
internal enum NaturePlan
{
    /// <summary>Inspection par un compte du site, avec la grille du corps d'état.</summary>
    InspectionInterne = 1,

    /// <summary>Vérification par un organisme agréé, un technicien compétent ou une entreprise, avec un rapport.</summary>
    VerificationExterieure = 2,

    /// <summary>Entretien sans grille : compte rendu et mesures facultatives.</summary>
    Entretien = 3,
}

/// <summary>Fondement d'un plan : texte réglementaire [R] ou bonne pratique [P] ; la référence reste « à confirmer ».</summary>
internal enum BasePlan
{
    Reglementaire = 1,
    BonnePratique = 2,
}

/// <summary>Qui réalise habituellement le contrôle proposé (information, sans effet sur les droits).</summary>
internal enum TypeIntervenant
{
    TechnicienSite = 1,
    TechnicienCompetent = 2,
    OrganismeAgree = 3,
    Entreprise = 4,
    CommissionSecurite = 5,
}

internal enum UnitePeriode
{
    Semaines = 1,
    Mois = 2,
    Ans = 3,
}

/// <summary>
/// Échéance glissante (par défaut) : date réelle de réalisation + période, ce qui garantit l'intervalle maximal ; fixe :
/// même date à chaque période depuis la première échéance, pour les tâches saisonnières.
/// </summary>
internal enum ModeEcheance
{
    Glissant = 1,
    Fixe = 2,
}

/// <summary>Ce que vise un plan : un composant (ou un lot), une zone pour un corps d'état, ou un bâtiment.</summary>
internal enum CiblePlan
{
    Composant = 1,
    ZoneCorpsEtat = 2,
    Batiment = 3,
}

/// <summary>Période d'un plan : tous les N semaines, mois ou ans.</summary>
internal sealed record Periode(int Valeur, UnitePeriode Unite)
{
    internal const int ValeurMax = 120;

    internal static Periode Valider(int? valeur, UnitePeriode? unite)
    {
        if (unite is not { } u || !Enum.IsDefined(u))
        {
            throw new SaisieInvalideException("periode.unite", "Unité de période : Semaines, Mois ou Ans.");
        }

        return valeur is >= 1 and <= ValeurMax
            ? new Periode(valeur.Value, u)
            : throw new SaisieInvalideException("periode.valeur", $"Période de 1 à {ValeurMax}.");
    }

    /// <summary>
    /// Date décalée de <paramref name="fois"/> périodes, comptées depuis cette date (jamais de proche en proche) : un 31 ou
    /// un 29 février passe au dernier jour du mois quand il n'existe pas, sans dériver les fois suivantes.
    /// </summary>
    internal DateOnly Apres(DateOnly depuis, int fois = 1) => Unite switch
    {
        UnitePeriode.Semaines => depuis.AddDays(7 * Valeur * fois),
        UnitePeriode.Mois => depuis.AddMonths(Valeur * fois),
        _ => depuis.AddYears(Valeur * fois),
    };

    /// <summary>Date reculée d'une période.</summary>
    internal DateOnly Avant(DateOnly echeance) => Apres(echeance, -1);
}
