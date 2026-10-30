using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Patrimoine.Contracts;

namespace GestionSite.Modules.Maintenance.Preventif;

/// <summary>
/// Modèle de plan du catalogue : proposition d'expert, jamais une obligation établie par l'application. La référence
/// reste « à confirmer par un bureau de contrôle ou le SDIS » ; <paramref name="MoisFixe"/> donne le mois d'une tâche
/// saisonnière (échéance fixe, dernier jour du mois).
/// </summary>
internal sealed record ModelePlan(
    string Code, string Libelle, NaturePlan Nature, BasePlan Base, string Reference, TypeIntervenant Intervenant, Periode Periode, int? MoisFixe = null)
{
    internal ModeEcheance Mode => MoisFixe is null ? ModeEcheance.Glissant : ModeEcheance.Fixe;
}

/// <summary>
/// Plan proposé pour une cible du site : clé stable (modèle, cible) qui garde la trace d'un écart ou d'une confirmation,
/// et échéance suggérée (aujourd'hui, la prochaine saison, ou la dernière visite connue + la période).
/// </summary>
internal sealed record PlanPropose(
    string Cle, ModelePlan Modele, CiblePlan Cible, Guid BatimentId, Guid ZoneId, Guid? ComposantId, string? CorpsEtat, string LibelleCible, DateOnly Echeance);

/// <summary>
/// Catalogue des plans proposés (US-12, S12-01, choix de l'agent révisable) : par corps d'état pour chaque composant actif
/// soumis à vérification, et par bâtiment (registre, commission, défibrillateur, amiante, monument, chéneaux). Ajusté
/// au classement ERP déclaré et aux salariés présents : catégories 1 à 4, organisme agréé et visite périodique de la
/// commission ; 5e catégorie, technicien compétent et fréquence annuelle conseillée, pas de commission sans locaux à
/// sommeil. Toutes les valeurs sont à confirmer ; l'application n'affiche jamais « conforme ».
/// </summary>
internal static class Catalogue
{
    internal const string AConfirmer = " — à confirmer par un bureau de contrôle ou le SDIS";
    private const string Composant = "composant";
    private const string Batiment = "batiment";

    private static readonly Periode Mensuelle = new(1, UnitePeriode.Mois);
    private static readonly Periode Semestrielle = new(6, UnitePeriode.Mois);
    private static readonly Periode Annuelle = new(1, UnitePeriode.Ans);
    private static readonly Periode Biennale = new(2, UnitePeriode.Ans);
    private static readonly Periode Triennale = new(3, UnitePeriode.Ans);
    private static readonly Periode Quinquennale = new(5, UnitePeriode.Ans);

    /// <summary>Contexte réglementaire d'une cible : classement de son bâtiment et salariés présents sur le site.</summary>
    private sealed record Contexte(ClassementBatiment? Classement, bool Salaries)
    {
        internal bool Erp => Classement?.Erp == true;

        /// <summary>ERP de la 1re à la 4e catégorie : organisme agréé là où le texte l'impose, visite de la commission.</summary>
        internal bool Erp1a4 => Erp && Classement!.Categorie <= 4;

        internal bool Erp5 => Erp && Classement!.Categorie == 5;
    }

    internal static IReadOnlyList<PlanPropose> Proposer(EtablissementInventaire etablissement, IReadOnlyList<ComposantInventaire> composants, DateOnly aujourdhui)
    {
        var salaries = etablissement.SalariesPresents == true;
        var batiments = etablissement.Batiments.ToDictionary(b => b.Id);
        var propositions = new List<PlanPropose>();
        // Un bâtiment sans zone « Parties communes » (créée avec lui, jamais supprimée) n'a pas de lieu pour ses plans.
        foreach (var batiment in etablissement.Batiments.Where(b => b.PartiesCommunesId is not null))
        {
            var contexte = new Contexte(batiment.Classement, salaries);
            propositions.AddRange(PourBatiment(contexte).Select(m => new PlanPropose(
                Cle(m, Batiment, batiment.Id), m, CiblePlan.Batiment, batiment.Id, batiment.PartiesCommunesId!.Value, null, null, batiment.Nom,
                Echeance(m, aujourdhui, m.Code == "commission-visite" ? batiment.Classement?.DerniereVisiteCommission : null))));
        }

        foreach (var composant in composants.Where(c => c.SoumisAVerification && !c.Retire))
        {
            var contexte = new Contexte(batiments.GetValueOrDefault(composant.BatimentId)?.Classement, salaries);
            propositions.AddRange(PourComposant(composant.CorpsEtat, contexte).Select(m => new PlanPropose(
                Cle(m, Composant, composant.Id), m, CiblePlan.Composant, composant.BatimentId, composant.ZoneId, composant.Id, composant.CorpsEtat,
                $"{composant.Nom} ({composant.Zone})", Echeance(m, aujourdhui, null))));
        }

        return propositions;
    }

    /// <summary>Clé stable d'une proposition : « code:cible:identifiant ».</summary>
    internal static string Cle(ModelePlan modele, string cible, Guid id) => $"{modele.Code}:{cible}:{id:N}";

    // Suggestion : saison à venir pour une tâche fixe ; dernière visite + période si elle est connue ; sinon aujourd'hui.
    private static DateOnly Echeance(ModelePlan modele, DateOnly aujourdhui, DateOnly? derniere)
    {
        if (modele.MoisFixe is { } mois)
        {
            var saison = new DateOnly(aujourdhui.Year, mois, DateTime.DaysInMonth(aujourdhui.Year, mois));
            return saison >= aujourdhui ? saison : new DateOnly(aujourdhui.Year + 1, mois, DateTime.DaysInMonth(aujourdhui.Year + 1, mois));
        }

        return derniere is { } date ? modele.Periode.Apres(date) : aujourdhui;
    }

    private static IEnumerable<ModelePlan> PourBatiment(Contexte c)
    {
        if (c.Erp)
        {
            yield return new ModelePlan(
                "registre-securite", "Revue du registre de sécurité (vérifications, travaux, formation)", NaturePlan.Entretien, BasePlan.Reglementaire,
                "CCH, art. R.143-44 (ex-R.123-51) : registre de sécurité tenu à jour ; revue annuelle conseillée" + AConfirmer,
                TypeIntervenant.TechnicienSite, Annuelle);
        }

        // Visite périodique de la commission : catégories 1 à 4, et 5e catégorie avec locaux à sommeil (GE 4). Type V : 3 ans
        // en 1re et 2e catégorie, 5 ans ensuite ; autres types (J, O, R, U… souvent plus fréquents) : 3 ans par prudence.
        if (c.Erp1a4 || (c.Erp5 && c.Classement!.LocauxSommeil))
        {
            var lieuDeCulte = c.Classement!.Type == "V";
            yield return new ModelePlan(
                "commission-visite", "Visite périodique de la commission de sécurité", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
                "CCH, art. R.143-34 et suivants ; arrêté du 25 juin 1980, art. GE 4 : périodicité selon le type et la catégorie" +
                (lieuDeCulte ? " (type V : 3 ans en 1re et 2e catégorie, 5 ans ensuite)" : " (3 ans proposés par prudence pour ce type)") + AConfirmer,
                TypeIntervenant.CommissionSecurite, !lieuDeCulte || c.Classement.Categorie <= 2 ? Triennale : Quinquennale);
        }

        if (c.Erp1a4)
        {
            yield return new ModelePlan(
                "dae-maintenance", "Maintenance du défibrillateur (DAE)", NaturePlan.Entretien, BasePlan.Reglementaire,
                "Décret 2018-1186 (DAE obligatoire en ERP de 1re à 4e catégorie) ; Code de la santé publique, art. R.5212-25 (maintenance)" + AConfirmer,
                TypeIntervenant.Entreprise, Annuelle);
        }

        if (c.Classement?.PermisAvant1997 is "Oui" or "Inconnu")
        {
            yield return new ModelePlan(
                "amiante-surveillance", "Repérage amiante (DTA) et surveillance de l'état des matériaux", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
                "Code de la santé publique, art. R.1334-14 et suivants : DTA pour un permis antérieur au 1er juillet 1997, surveillance triennale des matériaux de la liste A" + AConfirmer,
                TypeIntervenant.TechnicienCompetent, Triennale);
        }

        if (c.Classement?.MonumentHistorique == true)
        {
            yield return new ModelePlan(
                "monument-clos-couvert", "Visite de l'état du clos et du couvert (édifice protégé)", NaturePlan.Entretien, BasePlan.BonnePratique,
                "Bonne pratique pour un monument historique (Code du patrimoine : travaux soumis à autorisation)" + AConfirmer,
                TypeIntervenant.Entreprise, Annuelle);
        }

        yield return new ModelePlan(
            "cheneaux-nettoyage", "Nettoyage des chéneaux, gouttières et descentes avant l'hiver", NaturePlan.Entretien, BasePlan.BonnePratique,
            "Bonne pratique d'entretien du couvert" + AConfirmer, TypeIntervenant.TechnicienSite, Annuelle, MoisFixe: 10);
    }

    private static IEnumerable<ModelePlan> PourComposant(string corpsEtat, Contexte c)
    {
        if (corpsEtat != "EclairageSecurite")
        {
            yield return new ModelePlan(
                "inspection-interne", "Inspection avec la grille du corps d'état", NaturePlan.InspectionInterne, BasePlan.BonnePratique,
                "Bonne pratique : suivi de l'état avec la grille du site (US-11)" + AConfirmer, TypeIntervenant.TechnicienSite, Annuelle);
        }

        var modele = corpsEtat switch
        {
            "CourantsForts" => Electricite(c),
            "EclairageSecurite" => EclairageVerification(c),
            "SecuriteIncendie" => Incendie(c),
            // Corps d'état large (chaudière, ventilation, radiateurs) : obligation seulement pour une chaudière, d'où [P] et une
            // référence qui le dit ; la proposition s'écarte « non concerné » pour un autre équipement.
            "ChauffageVentilation" => new ModelePlan(
                "chauffage-entretien", "Entretien annuel du chauffage ou de la ventilation", NaturePlan.Entretien, BasePlan.BonnePratique,
                "Chaudière de 4 à 400 kW : entretien annuel obligatoire (Code de l'environnement, art. R.224-41-4 et suivants)" + AConfirmer,
                TypeIntervenant.Entreprise, Annuelle),
            "Gaz" => c.Erp
                ? new ModelePlan(
                    "gaz-verification", "Vérification des installations de gaz", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
                    "Arrêté du 25 juin 1980, art. GZ 30 (1re à 4e catégorie) ; arrêté du 22 juin 1990, art. PE 4 (5e catégorie)" + AConfirmer,
                    TypeIntervenant.TechnicienCompetent, Annuelle)
                : new ModelePlan(
                    "gaz-verification", "Contrôle d'étanchéité et des organes de coupure du gaz", NaturePlan.VerificationExterieure, BasePlan.BonnePratique,
                    "Bonne pratique hors ERP" + AConfirmer, TypeIntervenant.Entreprise, Annuelle),
            "Ascenseurs" => new ModelePlan(
                "ascenseur-entretien", "Entretien de l'ascenseur (visite du contrat)", NaturePlan.Entretien, BasePlan.Reglementaire,
                "CCH, ex-art. R.125-2-1 (recodifié R.134-x) : contrat d'entretien, visite toutes les 6 semaines" + AConfirmer,
                TypeIntervenant.Entreprise, new Periode(6, UnitePeriode.Semaines)),
            "Paratonnerre" => new ModelePlan(
                "paratonnerre-verification", "Vérification complète du paratonnerre", NaturePlan.VerificationExterieure, BasePlan.BonnePratique,
                "NF C 17-102 et NF EN 62305-3 : vérification complète tous les 2 ans hors installation classée" + AConfirmer,
                TypeIntervenant.Entreprise, Biennale),
            "Campanaire" => new ModelePlan(
                "campanaire-entretien", "Visite du campaniste (cloches, battants, beffroi, commandes)", NaturePlan.Entretien, BasePlan.BonnePratique,
                "Bonne pratique : visite annuelle par une entreprise campanaire" + AConfirmer, TypeIntervenant.Entreprise, Annuelle),
            "MenuiseriesExterieures" => c.Salaries || c.Erp
                ? new ModelePlan(
                    "portes-automatiques", "Entretien et vérification des portes et portails automatiques", NaturePlan.Entretien, BasePlan.Reglementaire,
                    "Code du travail, art. R.4224-12 et arrêté du 21 décembre 1993 (salariés) ; arrêté du 25 juin 1980, art. CO 48 (ERP)" + AConfirmer,
                    TypeIntervenant.Entreprise, Semestrielle)
                : null,
            _ => null,
        };
        if (modele is not null)
        {
            yield return modele;
        }

        if (corpsEtat == "EclairageSecurite")
        {
            yield return new ModelePlan(
                "eclairage-essai", "Essai de fonctionnement de l'éclairage de sécurité", NaturePlan.InspectionInterne,
                c.Erp || c.Salaries ? BasePlan.Reglementaire : BasePlan.BonnePratique,
                "Arrêté du 25 juin 1980, art. EC 14 : essai mensuel du passage en position de sécurité" + AConfirmer, TypeIntervenant.TechnicienSite, Mensuelle);
        }

        if (corpsEtat == "ChauffageVentilation" && c.Erp)
        {
            yield return new ModelePlan(
                "chauffage-verification", "Vérification des installations de chauffage et de ventilation", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
                "Arrêté du 25 juin 1980, art. CH 58 ; arrêté du 22 juin 1990, art. PE 4 (5e catégorie)" + AConfirmer, TypeIntervenant.TechnicienCompetent, Annuelle);
        }

        if (corpsEtat == "Ascenseurs")
        {
            yield return new ModelePlan(
                "ascenseur-controle-technique", "Contrôle technique quinquennal de l'ascenseur", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
                "CCH, ex-art. R.125-2-4 (recodifié R.134-x) : contrôle technique tous les 5 ans" + AConfirmer, TypeIntervenant.OrganismeAgree, Quinquennale);
        }
    }

    // Électricité : organisme agréé en 1re à 4e catégorie (EL 19), technicien compétent en 5e (PE 4, annuelle conseillée),
    // personne qualifiée pour un lieu de travail (Code du travail) ; sinon l'inspection interne suffit à proposer.
    private static ModelePlan? Electricite(Contexte c) => c switch
    {
        { Erp1a4: true } => new ModelePlan(
            "electricite-verification", "Vérification périodique des installations électriques", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
            "Arrêté du 25 juin 1980, art. EL 19 : vérification annuelle par un organisme agréé" + AConfirmer, TypeIntervenant.OrganismeAgree, Annuelle),
        { Erp5: true } => new ModelePlan(
            "electricite-verification", "Vérification des installations électriques", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
            "Arrêté du 22 juin 1990, art. PE 4 : technicien compétent, fréquence annuelle conseillée" + (c.Salaries ? " ; Code du travail, art. R.4226-16" : string.Empty)
                + AConfirmer,
            TypeIntervenant.TechnicienCompetent, Annuelle),
        { Salaries: true } => new ModelePlan(
            "electricite-verification", "Vérification annuelle des installations électriques (lieu de travail)", NaturePlan.VerificationExterieure,
            BasePlan.Reglementaire, "Code du travail, art. R.4226-16 et arrêté du 26 décembre 2011" + AConfirmer, TypeIntervenant.TechnicienCompetent, Annuelle),
        _ => null,
    };

    private static ModelePlan? EclairageVerification(Contexte c) => c switch
    {
        { Erp1a4: true } => new ModelePlan(
            "eclairage-verification", "Vérification de l'éclairage de sécurité", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
            "Arrêté du 25 juin 1980, art. EC 15 et EL 19 : organisme agréé" + AConfirmer, TypeIntervenant.OrganismeAgree, Annuelle),
        { Erp5: true } or { Salaries: true } => new ModelePlan(
            "eclairage-verification", "Vérification de l'éclairage de sécurité", NaturePlan.VerificationExterieure, BasePlan.Reglementaire,
            "Arrêté du 22 juin 1990, art. PE 4 et PE 24 ; Code du travail, art. R.4227-14 (salariés)" + AConfirmer, TypeIntervenant.TechnicienCompetent, Annuelle),
        _ => null,
    };

    private static ModelePlan Incendie(Contexte c) => c switch
    {
        { Erp: true } => new ModelePlan(
            "incendie-verification", "Vérification des moyens de secours (extincteurs, alarme, désenfumage)", NaturePlan.VerificationExterieure,
            BasePlan.Reglementaire, "Arrêté du 25 juin 1980, art. MS 38, MS 73 et DF 10 ; arrêté du 22 juin 1990, art. PE 26 (5e catégorie)" + AConfirmer,
            TypeIntervenant.TechnicienCompetent, Annuelle),
        { Salaries: true } => new ModelePlan(
            "incendie-verification", "Vérification des extincteurs et de l'alarme (lieu de travail)", NaturePlan.VerificationExterieure,
            BasePlan.Reglementaire, "Code du travail, art. R.4227-29 et R.4227-39" + AConfirmer, TypeIntervenant.TechnicienCompetent, Annuelle),
        _ => new ModelePlan(
            "incendie-verification", "Vérification des extincteurs", NaturePlan.VerificationExterieure, BasePlan.BonnePratique,
            "Règle APSAD R4 : vérification annuelle" + AConfirmer, TypeIntervenant.Entreprise, Annuelle),
    };
}
