using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>
/// Fond d'un plan de bâtiment importé en image (US-21, RP-21.3, S21-04) : maître de 8 000 px au plus et dérivé d'affichage
/// de 4 096 px au plus, réencodés sans métadonnées, stockés en privé sous l'organisation. Immuable et jamais supprimé :
/// une version de plan pourra s'y référer (calibration, S21-07) ; un nouvel import crée un nouveau fond.
/// </summary>
internal sealed class FondPlanBatiment : IAppartientAOrganisation
{
    internal const int CleMax = 200;
    internal const int TypeMax = 20;
    internal const string IndexCommande = "ix_fonds_plan_batiment_commande";

    private FondPlanBatiment()
    {
    }

    internal FondPlanBatiment(
        Guid id, PlanBatiment plan, Guid commandeId, string typeContenu, (int Largeur, int Hauteur, int Taille) maitre,
        (int Largeur, int Hauteur, int Taille) affichage, Guid auteurCompteId, DateTimeOffset creeLe)
    {
        Id = id;
        OrganisationId = plan.OrganisationId;
        SiteId = plan.SiteId;
        PlanId = plan.Id;
        CommandeId = commandeId;
        TypeContenu = typeContenu;
        (Largeur, Hauteur, TailleMaitre) = maitre;
        (LargeurAffichage, HauteurAffichage, TailleAffichage) = affichage;
        var extension = typeContenu == "image/png" ? "png" : "jpg";
        CleMaitre = $"organisations/{plan.OrganisationId:D}/plans-batiment/{plan.Id:D}/fonds/{id:D}-maitre.{extension}";
        CleAffichage = $"organisations/{plan.OrganisationId:D}/plans-batiment/{plan.Id:D}/fonds/{id:D}-affichage.{extension}";
        AuteurCompteId = auteurCompteId;
        CreeLe = creeLe;
    }

    public Guid Id { get; private set; }

    public Guid OrganisationId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid CommandeId { get; private set; }

    /// <summary>« image/png » pour un PNG importé, « image/jpeg » sinon (maître et dérivé).</summary>
    public string TypeContenu { get; private set; } = string.Empty;

    public int Largeur { get; private set; }

    public int Hauteur { get; private set; }

    public int TailleMaitre { get; private set; }

    public int LargeurAffichage { get; private set; }

    public int HauteurAffichage { get; private set; }

    public int TailleAffichage { get; private set; }

    public string CleMaitre { get; private set; } = string.Empty;

    public string CleAffichage { get; private set; } = string.Empty;

    public Guid AuteurCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}
