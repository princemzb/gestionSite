using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Preventif;

/// <summary>Proposition en attente de décision, ou écartée avec son motif (elle reste visible).</summary>
internal enum StatutProposition
{
    Proposee = 1,
    Ecartee = 2,
}

internal sealed record PeriodeLue(int Valeur, string Unite);

internal sealed record CibleLue(string Type, Guid BatimentId, Guid ZoneId, Guid? ComposantId, string? CorpsEtat, string Libelle);

internal sealed record EcartLu(string Motif, string? Commentaire, DateTimeOffset Le, Guid AuteurCompteId, string? Auteur);

internal sealed record PropositionLue(
    string Cle, string Modele, string Libelle, string Nature, string Base, string Reference, bool AConfirmer, string Intervenant, PeriodeLue Periode,
    string Mode, DateOnly Echeance, CibleLue Cible, string Statut, EcartLu? Ecart = null);

/// <summary>
/// Plans proposés d'un site (US-12) : catalogue appliqué au classement déclaré des bâtiments et aux composants soumis
/// à vérification, lus par le contrat de l'inventaire. Une proposition confirmée disparaît (le plan existe) ; écartée,
/// elle reste visible avec son motif (décision du 30/10). Consultés par les comptes internes du site ; rien n'est créé
/// sans confirmation de R/D.
/// </summary>
internal sealed class ServicePropositions(
    MaintenanceDbContext context, IAutorisations autorisations, IInventaireSite inventaire, IAnnuaireMembres annuaire, TimeProvider horloge)
{
    internal async Task<IReadOnlyList<PropositionLue>> ListerAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var etablissement = await inventaire.EtablissementAsync(siteId, cancellationToken).ConfigureAwait(false);
        var composants = await inventaire.ComposantsAsync(siteId, ids: null, cancellationToken).ConfigureAwait(false);
        var planifiees = (await context.Plans.AsNoTracking().Where(p => p.SiteId == siteId && p.PropositionCle != null).Select(p => p.PropositionCle!)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var ecarts = await context.PropositionsEcartees.AsNoTracking().Where(e => e.SiteId == siteId)
            .ToDictionaryAsync(e => e.Cle, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        var auteurs = await annuaire.NomsAsync([.. ecarts.Values.Select(e => e.AuteurCompteId).Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. Catalogue.Proposer(etablissement, composants, JourCivil.Du(horloge.GetUtcNow()))
            .Where(p => !planifiees.Contains(p.Cle))
            .Select(p => ecarts.TryGetValue(p.Cle, out var e)
                ? Lire(p, StatutProposition.Ecartee) with
                {
                    Ecart = new EcartLu(e.Motif.ToString(), e.Commentaire, e.EcarteeLe, e.AuteurCompteId, auteurs.GetValueOrDefault(e.AuteurCompteId)),
                }
                : Lire(p, StatutProposition.Proposee))];
    }

    internal static PropositionLue Lire(PlanPropose p, StatutProposition statut) => new(
        p.Cle, p.Modele.Code, p.Modele.Libelle, p.Modele.Nature.ToString(), p.Modele.Base.ToString(), p.Modele.Reference, AConfirmer: true,
        p.Modele.Intervenant.ToString(), new PeriodeLue(p.Modele.Periode.Valeur, p.Modele.Periode.Unite.ToString()), p.Modele.Mode.ToString(), p.Echeance,
        new CibleLue(p.Cible.ToString(), p.BatimentId, p.ZoneId, p.ComposantId, p.CorpsEtat, p.LibelleCible), statut.ToString());
}
