using System.Text.Json;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Maintenance.Domain;

/// <summary>Motif d'un saut (S12-03) ; « Autre » exige un commentaire. Une réalisation hors application se saisit après coup.</summary>
internal enum MotifSaut
{
    /// <summary>Installation hors service ou déposée.</summary>
    HorsService = 1,
    Inaccessible = 2,
    Autre = 3,
}

/// <summary>Résultat global déclaré par l'intervenant ou repris du rapport (jamais un « conforme à la réglementation » de l'application).</summary>
internal enum ResultatControle
{
    Conforme = 1,
    AvecObservations = 2,
    NonConforme = 3,
}

/// <summary>
/// Occurrence d'un plan, matérialisée seulement à l'affectation, à la réalisation ou au saut (clé unique : plan et
/// échéance prévue). Affectée, elle reste l'échéance courante même si le calendrier change ; réalisée ou sautée, elle
/// ne change plus.
/// </summary>
internal sealed class OccurrencePreventive : IAppartientAOrganisation
{
    internal const string ContrainteEcheance = "ix_occurrences_preventives_echeance";

    private OccurrencePreventive()
    {
    }

    internal OccurrencePreventive(Guid id, PlanPreventif plan, DateOnly echeance, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = plan.OrganisationId;
        SiteId = plan.SiteId;
        PlanId = plan.Id;
        Echeance = echeance;
        Statut = StatutOccurrence.Affectee;
        CreeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid PlanId { get; private set; }

    public DateOnly Echeance { get; private set; }

    public StatutOccurrence Statut { get; private set; }

    public Guid? IntervenantCompteId { get; private set; }

    public string? Prestataire { get; private set; }

    public Guid? ReferentCompteId { get; private set; }

    public DateTimeOffset? AffecteeLe { get; private set; }

    /// <summary>Date réelle de réalisation (déclarée), point de départ de l'échéance suivante en mode glissant.</summary>
    public DateOnly? RealiseeLe { get; private set; }

    public DateOnly? SauteeLe { get; private set; }

    public MotifSaut? MotifSaut { get; private set; }

    public string? CommentaireSaut { get; private set; }

    public Guid? ControleId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal bool Resolue => Statut != StatutOccurrence.Affectee;

    internal OccurrenceConnue Connue => new(Echeance, Statut, RealiseeLe ?? SauteeLe);

    /// <summary>Le compte est en charge : intervenant interne, ou référent du prestataire.</summary>
    internal bool EnCharge(Guid? compteId) => compteId is not null && (IntervenantCompteId == compteId || ReferentCompteId == compteId);

    /// <summary>Affectation ou réaffectation tant que l'occurrence n'est pas résolue ; faux si rien ne change.</summary>
    internal bool Affecter(IntervenantPlan intervenant, DateTimeOffset le)
    {
        ExigerOuverte();
        if (intervenant.CompteId is null && intervenant.Prestataire is null)
        {
            throw new SaisieInvalideException("intervenantCompteId", "Un technicien du site, ou un prestataire et son référent.");
        }

        if (AffecteeLe is not null && (IntervenantCompteId, Prestataire, ReferentCompteId) == (intervenant.CompteId, intervenant.Prestataire, intervenant.ReferentCompteId))
        {
            return false;
        }

        IntervenantCompteId = intervenant.CompteId;
        Prestataire = intervenant.Prestataire;
        ReferentCompteId = intervenant.ReferentCompteId;
        AffecteeLe = le;
        return true;
    }

    internal void Sauter(MotifSaut motif, string? commentaire, DateOnly le)
    {
        ExigerOuverte();
        if (!Enum.IsDefined(motif))
        {
            throw new SaisieInvalideException("motif", "Motif : HorsService, Inaccessible ou Autre.");
        }

        CommentaireSaut = Commentaire.Exiger(commentaire, motif == Domain.MotifSaut.Autre);
        MotifSaut = motif;
        SauteeLe = le;
        Statut = StatutOccurrence.Sautee;
    }

    internal void Realiser(ControlePreventif controle, bool reserves)
    {
        ExigerOuverte();
        ControleId = controle.Id;
        RealiseeLe = controle.RealiseLe;
        Statut = reserves ? StatutOccurrence.RealiseeAvecReserves : StatutOccurrence.Realisee;
    }

    private void ExigerOuverte()
    {
        if (Resolue)
        {
            throw new PlanIncompatibleException("Cette échéance est déjà réalisée ou sautée.");
        }
    }
}

/// <summary>Point de la grille présenté et son constat (vrai conforme, faux défaut, null non vérifié), danger signalé sur un défaut.</summary>
internal sealed record PointControle(string Texte, bool? Conforme, bool Danger);

/// <summary>Mesure relevée : de la grille (inspection) ou libre (entretien, vérification).</summary>
internal sealed record MesureControle(string Libelle, string Unite, decimal? Valeur);

/// <summary>Saisie d'un contrôle, avant contrôle (plan, grille et occurrence connus du service).</summary>
internal sealed record SaisieControle(
    Guid CommandeId, DateOnly? RealiseLe, ResultatControle? Resultat, string? CompteRendu, int? GrilleVersion,
    IReadOnlyList<(bool? Conforme, bool Danger)>? Points, IReadOnlyList<decimal?>? Mesures, IReadOnlyList<(string? Libelle, string? Unite, decimal? Valeur)>? Releves,
    string? Prestataire, string? NumeroAgrement, IReadOnlyList<(string? Libelle, Guid? ZoneId, bool Danger)>? Observations = null);

/// <summary>Grille présentée pour une inspection : version et contenu en vigueur sur le site.</summary>
internal sealed record GrilleControle(int Version, IReadOnlyList<string> Criteres, IReadOnlyList<(string Libelle, string Unite)> Mesures);

/// <summary>
/// Contrôle réalisé (US-12, S12-03) : date de réalisation déclarée et instant serveur de la saisie (comme US-11 avant
/// US-14), auteur, intervenant (compte interne, ou prestataire nommé saisi par son référent), résultat global,
/// compte rendu, points de la grille figés (version et texte) ou relevés libres. Jamais modifié ensuite.
/// </summary>
internal sealed class ControlePreventif : IAppartientAOrganisation
{
    internal const int CompteRenduMax = 2000;
    internal const int AgrementMax = 60;
    internal const int RelevesMax = 8;
    internal const int LibelleReleveMax = 80;
    internal const int UniteMax = 16;
    internal const decimal MesureMax = 1_000_000m;
    internal const string ContrainteCommande = "ix_controles_preventifs_commande";

    private ControlePreventif()
    {
    }

    private ControlePreventif(Guid id, PlanPreventif plan, CibleDuPlan cible, OccurrencePreventive occurrence, SaisieControle saisie, Guid auteur, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = plan.OrganisationId;
        SiteId = plan.SiteId;
        PlanId = plan.Id;
        OccurrenceId = occurrence.Id;
        Echeance = occurrence.Echeance;
        CommandeId = saisie.CommandeId;
        Nature = plan.Nature;
        CorpsEtat = cible.CorpsEtat;
        ComposantId = cible.ComposantId;
        ZoneId = cible.ZoneId;
        BatimentId = cible.BatimentId;
        AuteurCompteId = auteur;
        SaisiLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid OccurrenceId { get; private set; }

    public DateOnly Echeance { get; private set; }

    public Guid CommandeId { get; private set; }

    public NaturePlan Nature { get; private set; }

    public string? CorpsEtat { get; private set; }

    public Guid? ComposantId { get; private set; }

    public Guid ZoneId { get; private set; }

    public Guid BatimentId { get; private set; }

    public DateOnly RealiseLe { get; private set; }

    public DateTimeOffset SaisiLe { get; private set; }

    public Guid AuteurCompteId { get; private set; }

    /// <summary>Compte interne qui a réalisé le contrôle ; null pour un prestataire.</summary>
    public Guid? IntervenantCompteId { get; private set; }

    public string? Prestataire { get; private set; }

    public string? NumeroAgrement { get; private set; }

    public ResultatControle Resultat { get; private set; }

    public string? CompteRendu { get; private set; }

    public int? GrilleVersion { get; private set; }

    public string Points { get; private set; } = "[]";

    public string Mesures { get; private set; } = "[]";

    /// <summary>Évaluation US-11 enregistrée par une inspection de composant (décision du 30/10), note affichée et libellé.</summary>
    public Guid? EvaluationId { get; private set; }

    public decimal? Note { get; private set; }

    public string? LibelleNote { get; private set; }

    internal IReadOnlyList<PointControle> PointsLus => JsonSerializer.Deserialize<List<PointControle>>(Points) ?? [];

    internal IReadOnlyList<MesureControle> MesuresLues => JsonSerializer.Deserialize<List<MesureControle>>(Mesures) ?? [];

    /// <summary>Observations du rapport (vérification extérieure), chacune un écart à lever.</summary>
    public string Observations { get; private set; } = "[]";

    /// <summary>Rapport PDF (S12-04) : clé de stockage, taille et empreinte SHA-256 ; null sans rapport.</summary>
    public string? RapportCle { get; private set; }

    public int? RapportTaille { get; private set; }

    public string? RapportEmpreinte { get; private set; }

    internal IReadOnlyList<ObservationControle> ObservationsLues => JsonSerializer.Deserialize<List<ObservationControle>>(Observations) ?? [];

    /// <summary>Points en défaut : chacun est un constat à lever (S12-05).</summary>
    internal int Defauts => PointsLus.Count(p => p.Conforme == false) + ObservationsLues.Count;

    /// <summary>Réalisé avec réserves : un écart constaté, ou un résultat « non conforme » déclaré.</summary>
    internal bool Reserves => Defauts > 0 || Resultat == ResultatControle.NonConforme;

    /// <summary>
    /// Contrôle validé : résultat, date réelle (pas dans le futur, 5 ans au plus), compte rendu exigé pour un entretien ;
    /// inspection : grille en vigueur (version lue), un constat par point au plus (manquants : non vérifiés), danger sur
    /// un défaut seulement, mesures de la grille ; sinon relevés libres. Un défaut exclut le résultat « Conforme ».
    /// </summary>
    internal static ControlePreventif Creer(
        Guid id, PlanPreventif plan, CibleDuPlan cible, OccurrencePreventive occurrence, GrilleControle? grille, SaisieControle saisie, Guid auteur,
        DateTimeOffset le, IntervenantPlan? affecte = null)
    {
        var controle = new ControlePreventif(id, plan, cible, occurrence, saisie, auteur, le);
        var aujourdhui = JourCivil.Du(le);
        controle.RealiseLe = saisie.RealiseLe is { } date && date <= aujourdhui && date >= aujourdhui.AddYears(-5)
            ? date
            : throw new SaisieInvalideException("realiseLe", "Date de réalisation passée, de 5 ans au plus.");
        controle.Resultat = saisie.Resultat is { } resultat && Enum.IsDefined(resultat)
            ? resultat
            : throw new SaisieInvalideException("resultat", "Résultat : Conforme, AvecObservations ou NonConforme.");
        controle.CompteRendu = string.IsNullOrWhiteSpace(saisie.CompteRendu) ? null : TexteAffichable.Nettoyer(saisie.CompteRendu, CompteRenduMax, multiligne: true)
            ?? throw new SaisieInvalideException("compteRendu", $"Compte rendu : texte visible de {CompteRenduMax} caractères au plus.");
        if (plan.Nature == NaturePlan.Entretien && controle.CompteRendu is null)
        {
            throw new SaisieInvalideException("compteRendu", "Un entretien se termine par un compte rendu.");
        }

        // Intervenant : le prestataire saisi, sinon celui de l'affectation ; sinon le technicien affecté, sinon l'auteur.
        controle.Prestataire = string.IsNullOrWhiteSpace(saisie.Prestataire)
            ? affecte?.Prestataire
            : TexteAffichable.Nettoyer(saisie.Prestataire, Intervention.PrestataireMax)
                ?? throw new SaisieInvalideException("prestataire", $"prestataire : une ligne visible de {Intervention.PrestataireMax} caractères au plus.");
        controle.NumeroAgrement = string.IsNullOrWhiteSpace(saisie.NumeroAgrement) ? null : TexteAffichable.Nettoyer(saisie.NumeroAgrement, AgrementMax)
            ?? throw new SaisieInvalideException("numeroAgrement", $"numeroAgrement : une ligne visible de {AgrementMax} caractères au plus.");
        if (controle.NumeroAgrement is not null && controle.Prestataire is null)
        {
            throw new SaisieInvalideException("numeroAgrement", "Le numéro d'agrément est celui d'un prestataire.");
        }

        controle.IntervenantCompteId = controle.Prestataire is null ? affecte?.CompteId ?? auteur : null;
        if (plan.Nature == NaturePlan.InspectionInterne)
        {
            controle.Inspecter(grille ?? throw new PlanIncompatibleException("Grille requise pour une inspection."), saisie);
        }
        else
        {
            controle.Relever(saisie);
        }

        controle.Observer(plan.Nature, cible.ZoneId, saisie.Observations ?? []);
        if (controle.Defauts > 0 && controle.Resultat == ResultatControle.Conforme)
        {
            throw new SaisieInvalideException("resultat", "Un point en défaut ou une observation exclut le résultat « Conforme ».");
        }

        return controle;
    }

    /// <summary>Rapport PDF déjà contrôlé (RapportControle.Valider), stocké sous une clé propre à ce contrôle.</summary>
    internal void Joindre(Guid fichierId, int taille, string empreinte)
    {
        RapportCle = RapportControle.Cle(OrganisationId, Id, fichierId);
        RapportTaille = taille;
        RapportEmpreinte = empreinte;
    }

    // Observations d'un rapport de vérification : libellé, zone (celle du plan à défaut), danger ; une par écart à lever.
    private void Observer(NaturePlan nature, Guid zoneParDefaut, IReadOnlyList<(string? Libelle, Guid? ZoneId, bool Danger)> observations)
    {
        if (observations.Count > 0 && nature != NaturePlan.VerificationExterieure)
        {
            throw new SaisieInvalideException("observations", "Les observations sont celles d'un rapport de vérification extérieure.");
        }

        if (observations.Count > RapportControle.ObservationsMax)
        {
            throw new SaisieInvalideException("observations", $"{RapportControle.ObservationsMax} observations au plus.");
        }

        Observations = JsonSerializer.Serialize(observations.Select(o => new ObservationControle(
            TexteAffichable.Nettoyer(o.Libelle, RapportControle.LibelleObservationMax, multiligne: true)
                ?? throw new SaisieInvalideException("observations", $"Observation : texte visible de {RapportControle.LibelleObservationMax} caractères au plus."),
            o.ZoneId ?? zoneParDefaut, o.Danger)).ToList());
    }

    /// <summary>Évaluation US-11 enregistrée avec l'inspection d'un composant.</summary>
    internal void Evaluer(Guid evaluationId, decimal note, string libelle)
    {
        EvaluationId = evaluationId;
        Note = note;
        LibelleNote = libelle;
    }

    /// <summary>Même envoi rejoué : même auteur, même occurrence (plan et échéance), même résultat et même date réelle.</summary>
    internal bool MemeEnvoi(Guid auteur, Guid planId, DateOnly echeance, SaisieControle saisie) =>
        (AuteurCompteId, PlanId, Echeance, (ResultatControle?)Resultat, (DateOnly?)RealiseLe) == (auteur, planId, echeance, saisie.Resultat, saisie.RealiseLe);

    private void Inspecter(GrilleControle grille, SaisieControle saisie)
    {
        if (saisie.GrilleVersion is not { } version)
        {
            throw new SaisieInvalideException("grilleVersion", "Version de la grille utilisée obligatoire.");
        }

        if (version != grille.Version)
        {
            throw new GrilleControleModifieeException();
        }

        if (saisie.Releves is { Count: > 0 })
        {
            throw new SaisieInvalideException("releves", "Une inspection relève les mesures de la grille.");
        }

        var points = saisie.Points ?? [];
        if (points.Count > grille.Criteres.Count)
        {
            throw new SaisieInvalideException("points", $"{grille.Criteres.Count} constats au plus, un par point de la grille.");
        }

        if (points.Any(p => p.Danger && p.Conforme != false))
        {
            throw new SaisieInvalideException("points", "Un danger se signale sur un point en défaut.");
        }

        var mesures = saisie.Mesures ?? [];
        if (mesures.Count > grille.Mesures.Count)
        {
            throw new SaisieInvalideException("mesures", $"{grille.Mesures.Count} mesures au plus, une par mesure de la grille.");
        }

        GrilleVersion = version;
        Points = JsonSerializer.Serialize(grille.Criteres.Select((texte, i) => i < points.Count
            ? new PointControle(texte, points[i].Conforme, points[i].Danger)
            : new PointControle(texte, null, false)).ToList());
        Mesures = JsonSerializer.Serialize(grille.Mesures.Select((m, i) => new MesureControle(m.Libelle, m.Unite, Valeur(i < mesures.Count ? mesures[i] : null, "mesures"))).ToList());
    }

    private void Relever(SaisieControle saisie)
    {
        var horsGrille = saisie.Points is { Count: > 0 } ? "points" : saisie.Mesures is { Count: > 0 } ? "mesures" : saisie.GrilleVersion is not null ? "grilleVersion" : null;
        if (horsGrille is not null)
        {
            throw new SaisieInvalideException(horsGrille, "Sans grille : relevés libres seulement.");
        }

        var releves = saisie.Releves ?? [];
        if (releves.Count > RelevesMax)
        {
            throw new SaisieInvalideException("releves", $"{RelevesMax} relevés au plus.");
        }

        Mesures = JsonSerializer.Serialize(releves.Select(r => new MesureControle(
            TexteAffichable.Nettoyer(r.Libelle, LibelleReleveMax) ?? throw new SaisieInvalideException("releves", $"Libellé de relevé de {LibelleReleveMax} caractères au plus."),
            TexteAffichable.Nettoyer(r.Unite, UniteMax) ?? throw new SaisieInvalideException("releves", $"Unité de {UniteMax} caractères au plus."),
            Valeur(r.Valeur, "releves"))).ToList());
    }

    private static decimal? Valeur(decimal? valeur, string champ) => valeur switch
    {
        null => null,
        > MesureMax or < -MesureMax => throw new SaisieInvalideException(champ, $"Mesure entre -{MesureMax} et {MesureMax}."),
        { } v when Math.Round(v, 3) != v => throw new SaisieInvalideException(champ, "Mesure : trois décimales au plus."),
        { } v => v / 1.000000000000000000000000000m, // zéros de fin retirés
    };
}

/// <summary>La grille a changé depuis son affichage (409 « grille_modifiee ») : relire la grille puis refaire l'inspection.</summary>
internal sealed class GrilleControleModifieeException() : Exception("La grille de ce corps d'état vient d'être modifiée : rechargez-la puis recommencez.");

/// <summary>Photo d'un contrôle (JPEG réencodé), sous une clé propre à ce contrôle.</summary>
internal sealed class PhotoControle : IAppartientAOrganisation
{
    internal const int CleMax = 200;

    private PhotoControle()
    {
    }

    internal PhotoControle(Guid id, ControlePreventif controle, int rang, int taille, DateTimeOffset le)
    {
        Id = id;
        OrganisationId = controle.OrganisationId;
        ControleId = controle.Id;
        Rang = rang;
        Cle = $"organisations/{controle.OrganisationId:D}/controles/{controle.Id:D}/{id:D}.jpg";
        Taille = taille;
        CreeLe = le;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid ControleId { get; private set; }

    public int Rang { get; private set; }

    public string Cle { get; private set; } = string.Empty;

    public int Taille { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}
