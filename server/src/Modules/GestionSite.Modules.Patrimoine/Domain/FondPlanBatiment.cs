using GestionSite.SharedKernel.Persistence;

namespace GestionSite.Modules.Patrimoine.Domain;

/// <summary>Page d'un PDF rendue en fond (S21-05) : page choisie, taille de la page en millimètres, échelle 1:N déclarée.</summary>
internal sealed record SourcePdf(int Page, int Pages, decimal LargeurPageMm, decimal HauteurPageMm, int? Echelle, int TailleOriginal);

/// <summary>
/// Fond d'un plan de bâtiment importé en image (US-21, RP-21.3, S21-04) ou rendu d'une page de PDF (S21-05) : maître de
/// 8 000 px au plus et dérivé d'affichage de 4 096 px au plus, réencodés sans métadonnées, stockés en privé sous
/// l'organisation ; pour un PDF, l'original est aussi conservé en privé (jamais servi) pour un nouveau rendu. Immuable et
/// jamais supprimé : une version de plan pourra s'y référer (calibration, S21-07) ; un nouvel import crée un nouveau fond.
/// </summary>
internal sealed class FondPlanBatiment : IAppartientAOrganisation
{
    internal const int CleMax = 200;
    internal const int TypeMax = 20;

    /// <summary>Échelle déclarée 1:N : N de 1 à 10 000 (du détail au plan de masse d'un grand site).</summary>
    internal const int EchelleMax = 10_000;
    internal const string IndexCommande = "ix_fonds_plan_batiment_commande";

    private FondPlanBatiment()
    {
    }

    internal FondPlanBatiment(
        Guid id, PlanBatiment plan, Guid commandeId, string typeContenu, (int Largeur, int Hauteur, int Taille) maitre,
        (int Largeur, int Hauteur, int Taille) affichage, SourcePdf? pdf, Guid auteurCompteId, DateTimeOffset creeLe)
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
        if (pdf is not null)
        {
            CleOriginal = $"organisations/{plan.OrganisationId:D}/plans-batiment/{plan.Id:D}/fonds/{id:D}-original.pdf";
            (PagePdf, PagesPdf, Echelle, TailleOriginal) = (pdf.Page, pdf.Pages, pdf.Echelle, pdf.TailleOriginal);
            (LargeurPageMm, HauteurPageMm) = (pdf.LargeurPageMm, pdf.HauteurPageMm);
        }

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

    /// <summary>PDF d'origine (S21-05), conservé en privé et jamais servi ; null pour une image importée.</summary>
    public string? CleOriginal { get; private set; }

    public int? TailleOriginal { get; private set; }

    /// <summary>Page rendue (à partir de 1) et nombre de pages du PDF.</summary>
    public int? PagePdf { get; private set; }

    public int? PagesPdf { get; private set; }

    /// <summary>Taille de la page rendue (après sa rotation), en millimètres au centième.</summary>
    public decimal? LargeurPageMm { get; private set; }

    public decimal? HauteurPageMm { get; private set; }

    /// <summary>Échelle 1:N déclarée à l'import (N), simple proposition pour la calibration (RP-21.4).</summary>
    public int? Echelle { get; private set; }

    /// <summary>Place occupée dans le stockage (quota) : maître, dérivé et PDF d'origine.</summary>
    public long Taille => (long)TailleMaitre + TailleAffichage + (TailleOriginal ?? 0);

    public Guid AuteurCompteId { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }
}
