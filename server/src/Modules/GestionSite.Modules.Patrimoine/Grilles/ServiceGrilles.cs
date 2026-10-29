using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Grilles;

internal sealed record MesureLue(string Libelle, string Unite);

internal sealed record GrilleLue(string CorpsEtat, int Version, bool Adaptee, IReadOnlyList<string> Criteres, IReadOnlyList<MesureLue> Mesures);

/// <summary>Grille utilisée par une évaluation : corps d'état, version, contenu.</summary>
internal sealed record GrilleCourante(CorpsEtat CorpsEtat, int Version, ContenuGrille Contenu);

/// <summary>
/// Grilles d'évaluation (US-11, S11-01) : celle de l'application (version 1) tant que le site ne l'a pas adaptée, puis
/// la dernière version enregistrée par R/D du site. Lecture par les comptes internes du site ; une modification crée
/// une version (PERMISSIONS), refusée si la version lue est dépassée.
/// </summary>
internal sealed class ServiceGrilles(PatrimoineDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal async Task<IReadOnlyList<GrilleLue>> ListerAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var adaptees = (await DernieresVersions(siteId).ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(g => g.CorpsEtat);
        return [.. GrillesParDefaut.CorpsEtats.Select(c => Lire(Courante(c, adaptees.GetValueOrDefault(c))))];
    }

    internal async Task<GrilleLue> LireAsync(Guid organisationId, Guid siteId, string corpsEtat, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        return Lire(await CouranteAsync(siteId, Corps(corpsEtat), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Nouvelle version à partir de la version lue ; sans changement, rien n'est écrit.</summary>
    internal async Task<int> ModifierAsync(Guid organisationId, Guid siteId, string corpsEtat, DemandeGrille demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierGrilles, cancellationToken).ConfigureAwait(false);
        var corps = Corps(corpsEtat);
        var contenu = VersionGrille.Valider(demande.Criteres, [.. (demande.Mesures ?? []).Select(m => (m?.Libelle, m?.Unite))]);
        var versionLue = demande.Version ?? throw new SaisieInvalideException("version", "Version lue obligatoire.");
        var courante = await CouranteAsync(siteId, corps, cancellationToken).ConfigureAwait(false);
        if (versionLue != courante.Version)
        {
            throw new ModifieEntreTempsException();
        }

        if (contenu.Criteres.SequenceEqual(courante.Contenu.Criteres) && contenu.Mesures.SequenceEqual(courante.Contenu.Mesures))
        {
            return courante.Version;
        }

        var maintenant = horloge.GetUtcNow();
        context.Grilles.Add(new VersionGrille(
            Guid.CreateVersion7(maintenant), context.OrganisationRequise, siteId, corps, courante.Version + 1, contenu, compte.Id, maintenant));
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return courante.Version + 1;
    }

    /// <summary>
    /// Grille en vigueur sur un site pour un corps d'état. L'appelant a vérifié l'accès au site, ce qui fixe
    /// l'organisation courante ; sans elle, l'appel échoue au lieu de rendre la grille par défaut.
    /// </summary>
    internal async Task<GrilleCourante> CouranteAsync(Guid siteId, CorpsEtat corps, CancellationToken cancellationToken)
    {
        _ = context.OrganisationRequise;
        var derniere = await DernieresVersions(siteId).FirstOrDefaultAsync(g => g.CorpsEtat == corps, cancellationToken).ConfigureAwait(false);
        return Courante(corps, derniere);
    }

    // Dernière version de chaque corps d'état adapté sur le site, calculée en base (index ix_grilles_version).
    private IQueryable<VersionGrille> DernieresVersions(Guid siteId) =>
        context.Grilles.AsNoTracking().Where(g => g.SiteId == siteId
            && !context.Grilles.Any(suivante => suivante.SiteId == siteId && suivante.CorpsEtat == g.CorpsEtat && suivante.Version > g.Version));

    private static GrilleCourante Courante(CorpsEtat corps, VersionGrille? adaptee) =>
        adaptee is null ? new GrilleCourante(corps, 1, GrillesParDefaut.Pour(corps)) : new GrilleCourante(corps, adaptee.Version, adaptee.Contenu);

    private static GrilleLue Lire(GrilleCourante grille) => new(
        grille.CorpsEtat.ToString(), grille.Version, grille.Version > 1, grille.Contenu.Criteres,
        [.. grille.Contenu.Mesures.Select(m => new MesureLue(m.Libelle, m.Unite))]);

    private static CorpsEtat Corps(string corpsEtat) =>
        NomExact.Essayer<CorpsEtat>(corpsEtat, out var corps) ? corps : throw new ElementIntrouvableException();
}
