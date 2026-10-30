using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Grilles;

internal sealed record MesureLue(string Libelle, string Unite);

// Adaptation : date et nom affiché de l'auteur de la version en vigueur (null pour la grille par défaut).
internal sealed record GrilleLue(
    string CorpsEtat, int Version, bool Adaptee, IReadOnlyList<string> Criteres, IReadOnlyList<MesureLue> Mesures, DateTimeOffset? AdapteeLe,
    string? Auteur);

/// <summary>Contenu par défaut d'une grille, proposé à R/D pour « revenir à la grille par défaut » (nouvelle version).</summary>
internal sealed record ContenuLu(IReadOnlyList<string> Criteres, IReadOnlyList<MesureLue> Mesures);

/// <summary>Grille utilisée par une évaluation : corps d'état, version, contenu.</summary>
internal sealed record GrilleCourante(CorpsEtat CorpsEtat, int Version, ContenuGrille Contenu);

/// <summary>
/// Grilles d'évaluation (US-11, S11-01) : celle de l'application (version 1) tant que le site ne l'a pas adaptée, puis
/// la dernière version enregistrée par R/D du site. Lecture par les comptes internes du site ; une modification crée
/// une version (PERMISSIONS), refusée si la version lue est dépassée.
/// </summary>
internal sealed class ServiceGrilles(
    PatrimoineDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, CompteCourant compte, TimeProvider horloge)
{
    internal async Task<IReadOnlyList<GrilleLue>> ListerAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var adaptees = (await DernieresVersions(siteId).ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(g => g.CorpsEtat);
        return await LireAsync([.. GrillesParDefaut.CorpsEtats.Select(c => (c, adaptees.GetValueOrDefault(c)))], cancellationToken).ConfigureAwait(false);
    }

    internal async Task<GrilleLue> LireAsync(Guid organisationId, Guid siteId, string corpsEtat, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var corps = Corps(corpsEtat);
        return (await LireAsync([(corps, await AdapteeAsync(siteId, corps, cancellationToken).ConfigureAwait(false))], cancellationToken)
            .ConfigureAwait(false))[0];
    }

    internal async Task<ContenuLu> ParDefautAsync(Guid organisationId, Guid siteId, string corpsEtat, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var contenu = GrillesParDefaut.Pour(Corps(corpsEtat));
        return new ContenuLu(contenu.Criteres, [.. contenu.Mesures.Select(m => new MesureLue(m.Libelle, m.Unite))]);
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
    internal Task<GrilleCourante> CouranteAsync(Guid siteId, CorpsEtat corps, CancellationToken cancellationToken) =>
        GrillesEnVigueur.CouranteAsync(context, siteId, corps, cancellationToken);

    private Task<VersionGrille?> AdapteeAsync(Guid siteId, CorpsEtat corps, CancellationToken cancellationToken) =>
        GrillesEnVigueur.AdapteeAsync(context, siteId, corps, cancellationToken);

    private IQueryable<VersionGrille> DernieresVersions(Guid siteId) => GrillesEnVigueur.DernieresVersions(context, siteId);

    private static GrilleCourante Courante(CorpsEtat corps, VersionGrille? adaptee) => GrillesEnVigueur.Courante(corps, adaptee);

    // Grilles lues avec le nom de l'auteur de chaque adaptation (une seule lecture de l'annuaire).
    private async Task<IReadOnlyList<GrilleLue>> LireAsync(IReadOnlyList<(CorpsEtat Corps, VersionGrille? Adaptee)> grilles, CancellationToken cancellationToken)
    {
        var auteurs = await annuaire.NomsAsync(
            [.. grilles.Select(g => g.Adaptee?.AuteurCompteId).OfType<Guid>().Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. grilles.Select(g =>
        {
            var courante = Courante(g.Corps, g.Adaptee);
            var auteur = g.Adaptee?.AuteurCompteId;
            return new GrilleLue(
                courante.CorpsEtat.ToString(), courante.Version, courante.Version > 1, courante.Contenu.Criteres,
                [.. courante.Contenu.Mesures.Select(m => new MesureLue(m.Libelle, m.Unite))], g.Adaptee?.CreeLe,
                auteur is { } id ? auteurs.GetValueOrDefault(id) : null);
        })];
    }

    private static CorpsEtat Corps(string corpsEtat) =>
        NomExact.Essayer<CorpsEtat>(corpsEtat, out var corps) ? corps : throw new ElementIntrouvableException();
}
