using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionSite.Modules.Patrimoine.PlansBatiment;

/// <summary>Réglages des fonds de plan de bâtiment (section « PlansBatiment »).</summary>
internal sealed class FondsPlanOptions
{
    internal const string Section = "PlansBatiment";

    /// <summary>
    /// Place occupée par les fonds (maîtres et dérivés) d'une organisation, en octets : 1 Gio par défaut, valeur proposée par
    /// l'agent (S21-04), à confirmer par le porteur.
    /// </summary>
    public long QuotaFondsOctets { get; init; } = 1024L * 1024 * 1024;
}

internal sealed record DemandeFond(Guid? CommandeId);

internal sealed record FondImporte(Guid Id, string TypeContenu, int Largeur, int Hauteur, int LargeurAffichage, int HauteurAffichage);

internal sealed record FondLu(
    Guid Id, string TypeContenu, int Largeur, int Hauteur, int LargeurAffichage, int HauteurAffichage, long Taille, DateTimeOffset Le, string? Auteur);

/// <summary>Quota de stockage des fonds de l'organisation atteint (422 « quota_atteint »).</summary>
internal sealed class QuotaFondsAtteintException()
    : Exception("Espace réservé aux fonds de plan de l'organisation plein : importez une image plus légère ou demandez à l'administrateur.");

/// <summary>
/// Fonds de plan de bâtiment importés en image (US-21, RP-21.3, S21-04) : import pour R/D et les titulaires du droit
/// nominatif (ModifierPlans), lecture pour les comptes internes du site (ConsulterSite). Les fichiers sont écrits avant
/// l'enregistrement en base et retirés s'il n'aboutit pas : aucun fichier orphelin d'un import refusé.
/// </summary>
internal sealed class ServiceFondsPlan(
    PatrimoineDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, IStockageFichiers stockage, CompteCourant compte,
    TimeProvider horloge, IOptions<FondsPlanOptions> options)
{
    /// <summary>
    /// Import d'une image de fond pour le plan. Le formulaire (<paramref name="lire"/>) n'est lu qu'après le contrôle d'accès
    /// et du plan : 403/404 avant toute erreur de saisie. Renvoi du même identifiant d'envoi : même fond, sans nouveau fichier.
    /// </summary>
    internal async Task<FondImporte> ImporterAsync(
        Guid organisationId, Guid siteId, Guid planId, Func<Task<(DemandeFond Demande, byte[] Fichier)>> lire, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierPlans, cancellationToken).ConfigureAwait(false);
        var plan = await context.PlansBatiment.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.SiteId == siteId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ElementIntrouvableException();
        var (demande, fichier) = await lire().ConfigureAwait(false);
        var commande = demande.CommandeId is { } id && id != Guid.Empty
            ? id
            : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");
        var auteur = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        if (await DejaRecuAsync(plan.Id, commande, auteur, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja;
        }

        // Quota déjà plein : refus avant tout décodage. Sinon, contrôlé de nouveau avec la taille réelle, sous verrou.
        var quota = options.Value.QuotaFondsOctets;
        if (await OccupeAsync(cancellationToken).ConfigureAwait(false) >= quota)
        {
            throw new QuotaFondsAtteintException();
        }

        var prepare = TraitementFonds.Preparer(fichier);
        var maintenant = horloge.GetUtcNow();
        var fond = new FondPlanBatiment(
            Guid.CreateVersion7(maintenant), plan, commande, prepare.TypeContenu, (prepare.Largeur, prepare.Hauteur, prepare.Maitre.Length),
            (prepare.LargeurAffichage, prepare.HauteurAffichage, prepare.Affichage.Length), auteur, maintenant);
        string[] cles = [fond.CleMaitre, fond.CleAffichage];
        await using var envoi = await PhotosStockees.EcrireAsync(
            stockage, [prepare.Maitre, prepare.Affichage], prepare.TypeContenu, (rang, _) => cles[rang - 1], cle => cle,
            _ => context.FondsPlanBatiment.AsNoTracking().AnyAsync(f => f.Id == fond.Id, CancellationToken.None), cancellationToken).ConfigureAwait(false);

        // Imports de l'organisation sérialisés le temps de la transaction : le quota n'est pas dépassé par deux imports
        // simultanés, et un même envoi reçu deux fois n'enregistre qu'un fond. Toute sortie sans confirmation (refus,
        // envoi concurrent, échec de la base) retire les fichiers écrits par cette requête.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"fonds-plan:{plan.OrganisationId:N}"}, 0))", cancellationToken).ConfigureAwait(false);
        if (await DejaRecuAsync(plan.Id, commande, auteur, cancellationToken).ConfigureAwait(false) is { } concurrent)
        {
            return concurrent;
        }

        if (await OccupeAsync(cancellationToken).ConfigureAwait(false) + prepare.Maitre.Length + prepare.Affichage.Length > quota)
        {
            throw new QuotaFondsAtteintException();
        }

        context.FondsPlanBatiment.Add(fond);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        envoi.Confirmer();
        return Importe(fond);
    }

    /// <summary>Fonds importés pour le plan, dans l'ordre d'import.</summary>
    internal async Task<IReadOnlyList<FondLu>> ListerAsync(Guid organisationId, Guid siteId, Guid planId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        if (!await context.PlansBatiment.AnyAsync(p => p.Id == planId && p.SiteId == siteId, cancellationToken).ConfigureAwait(false))
        {
            throw new ElementIntrouvableException();
        }

        var fonds = await context.FondsPlanBatiment.AsNoTracking().Where(f => f.PlanId == planId && f.SiteId == siteId)
            .OrderBy(f => f.CreeLe).ThenBy(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var noms = await annuaire.NomsAsync([.. fonds.Select(f => f.AuteurCompteId).Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. fonds.Select(f => new FondLu(
            f.Id, f.TypeContenu, f.Largeur, f.Hauteur, f.LargeurAffichage, f.HauteurAffichage, (long)f.TailleMaitre + f.TailleAffichage, f.CreeLe,
            noms.GetValueOrDefault(f.AuteurCompteId)))];
    }

    /// <summary>Image d'affichage du fond et son type, ou null si ce fond n'existe pas pour ce plan du site.</summary>
    internal async Task<(byte[] Contenu, string TypeContenu)?> ImageAsync(
        Guid organisationId, Guid siteId, Guid planId, Guid fondId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var fond = await context.FondsPlanBatiment.AsNoTracking()
            .Where(f => f.Id == fondId && f.PlanId == planId && f.SiteId == siteId).Select(f => new { f.CleAffichage, f.TypeContenu })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (fond is null || await stockage.LireAsync(fond.CleAffichage, cancellationToken).ConfigureAwait(false) is not { } fichier)
        {
            return null;
        }

        return (fichier.Contenu, fond.TypeContenu); // type enregistré, jamais celui rendu par le stockage
    }

    // Place occupée par les fonds de l'organisation courante (filtre de requête et RLS).
    private async Task<long> OccupeAsync(CancellationToken cancellationToken) =>
        await context.FondsPlanBatiment.SumAsync(f => (long)f.TailleMaitre + f.TailleAffichage, cancellationToken).ConfigureAwait(false);

    // Renvoi : même identifiant d'envoi, même plan, même compte → même fond ; sinon identifiant réutilisé à tort (409).
    private async Task<FondImporte?> DejaRecuAsync(Guid planId, Guid commande, Guid auteur, CancellationToken cancellationToken)
    {
        var recu = await context.FondsPlanBatiment.AsNoTracking().SingleOrDefaultAsync(f => f.CommandeId == commande, cancellationToken).ConfigureAwait(false);
        return recu switch
        {
            null => null,
            _ when recu.PlanId == planId && recu.AuteurCompteId == auteur => Importe(recu),
            _ => throw new EnvoiPlanReutiliseException(),
        };
    }

    private static FondImporte Importe(FondPlanBatiment fond) =>
        new(fond.Id, fond.TypeContenu, fond.Largeur, fond.Hauteur, fond.LargeurAffichage, fond.HauteurAffichage);
}
