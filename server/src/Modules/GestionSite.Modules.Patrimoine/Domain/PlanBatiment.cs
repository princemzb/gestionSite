using System.Text.Json;
using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Point du repère du bâtiment, en mètres, au centimètre (RP-21.2).</summary>
internal sealed record PointPlan(double X, double Y);

/// <summary>Contour d'une zone de l'inventaire sur le plan de son niveau (RP-21.5) : polygone simple, sommets non répétés.</summary>
internal sealed record PiecePlan(Guid ZoneId, IReadOnlyList<PointPlan> Contour);

/// <summary>Porte, baie ou fenêtre (RP-21.6) : segment posé sur une arête d'une pièce, reliant une ou deux zones.</summary>
internal sealed record OuverturePlan(string Type, PointPlan De, PointPlan A, IReadOnlyList<Guid> Zones);

/// <summary>Escalier ou ascenseur (RP-21.6) : contour qui relie ce niveau à un autre niveau du même bâtiment.</summary>
internal sealed record LiaisonPlan(string Type, IReadOnlyList<PointPlan> Contour, Guid VersNiveauId);

/// <summary>
/// Marqueur d'un composant actif (RP-21.7). La zone est celle du composant au moment de l'enregistrement : un composant
/// déplacé depuis est signalé à la lecture, son marqueur reste visible dans cette version.
/// </summary>
internal sealed record MarqueurPlan(Guid ComposantId, Guid ZoneId, PointPlan Position);

/// <summary>
/// Contenu d'une version de plan de bâtiment, schéma 1 : JSON stocké tel quel et rendu tel quel (lisible par le web et
/// par l'application tablette), renvoyable tel quel pour enregistrer la suivante. Nord en degrés depuis l'axe Y du repère, altitude du niveau et hauteur sous plafond en mètres.
/// </summary>
internal sealed record ContenuPlan(
    int Schema, double? Nord, double? Altitude, double? HauteurSousPlafond, IReadOnlyList<PiecePlan> Pieces, IReadOnlyList<OuverturePlan> Ouvertures,
    IReadOnlyList<LiaisonPlan> Escaliers, IReadOnlyList<MarqueurPlan> Composants)
{
    internal static ContenuPlan Vide { get; } = new(VersionPlanBatiment.SchemaCourant, null, null, null, [], [], [], []);
}

/// <summary>Avertissement d'un enregistrement accepté (marqueur hors du contour de sa zone, par exemple).</summary>
internal sealed record AvertissementPlan(string Code, Guid? ComposantId);

/// <summary>
/// Plan de bâtiment (US-21, RP-21.1) : un par niveau, ou un plan masse unique (niveau absent) pour un bâtiment sans
/// niveau. Son contenu est porté par ses versions, jamais modifiées.
/// </summary>
internal sealed class PlanBatiment : IAppartientAOrganisation
{
    internal const string ContrainteNiveau = "ix_plans_batiment_niveau";

    private PlanBatiment()
    {
    }

    internal PlanBatiment(Guid id, Batiment batiment, Niveau? niveau, Guid? auteurCompteId, DateTimeOffset creeLe)
    {
        Id = id;
        OrganisationId = batiment.OrganisationId;
        SiteId = batiment.SiteId;
        BatimentId = batiment.Id;
        NiveauId = niveau?.Id;
        AuteurCompteId = auteurCompteId;
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid BatimentId { get; private set; }

    public Guid? NiveauId { get; private set; }

    public Guid? AuteurCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}

/// <summary>
/// Version d'un plan de bâtiment (RP-21.8) : instantané complet, numéroté à partir de 1, immuable. Une restauration
/// recopie une version antérieure dans une nouvelle version (<see cref="RestaureDe"/>).
/// </summary>
internal sealed class VersionPlanBatiment : IAppartientAOrganisation
{
    internal const int SchemaCourant = 1;
    internal const int MotifMax = 500;
    internal const string ContrainteVersion = "ix_versions_plan_batiment_version";
    internal const string ContrainteCommande = "ix_versions_plan_batiment_commande";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private VersionPlanBatiment()
    {
    }

    internal VersionPlanBatiment(
        Guid id, PlanBatiment plan, int version, Guid commandeId, ContenuPlan contenu, IReadOnlyList<AvertissementPlan> avertissements,
        string? motif, int? restaureDe, Guid auteurCompteId, DateTimeOffset creeLe)
    {
        Id = id;
        OrganisationId = plan.OrganisationId;
        SiteId = plan.SiteId;
        PlanId = plan.Id;
        Version = version;
        Schema = SchemaCourant;
        CommandeId = commandeId;
        Contenu = JsonSerializer.Serialize(contenu, Json);
        Avertissements = JsonSerializer.Serialize(avertissements, Json);
        Motif = motif;
        RestaureDe = restaureDe;
        AuteurCompteId = auteurCompteId;
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid PlanId { get; private set; }

    public int Version { get; private set; }

    public int Schema { get; private set; }

    public Guid CommandeId { get; private set; }

    public string Contenu { get; private set; } = "{}";

    public string Avertissements { get; private set; } = "[]";

    public string? Motif { get; private set; }

    public int? RestaureDe { get; private set; }

    public Guid AuteurCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    internal ContenuPlan ContenuLu => JsonSerializer.Deserialize<ContenuPlan>(Contenu, Json) ?? ContenuPlan.Vide;

    internal IReadOnlyList<AvertissementPlan> AvertissementsLus => JsonSerializer.Deserialize<List<AvertissementPlan>>(Avertissements, Json) ?? [];
}
