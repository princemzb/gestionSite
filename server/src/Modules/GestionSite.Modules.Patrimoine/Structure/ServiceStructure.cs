using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Structure;

internal sealed record ZoneLue(Guid Id, string Nom, Guid? NiveauId, bool PartiesCommunes);

internal sealed record NiveauLu(Guid Id, string Nom, int Ordre);

internal sealed record BatimentLu(Guid Id, string Nom, IReadOnlyList<NiveauLu> Niveaux, IReadOnlyList<ZoneLue> Zones);

/// <summary>Refus traduit en 404 (hors périmètre de l'appelant) ou 403 (site visible, action non permise).</summary>
internal sealed class AccesInventaireRefuseException(DecisionAutorisation decision) : Exception("Accès refusé.")
{
    internal DecisionAutorisation Decision { get; } = decision;
}

/// <summary>Bâtiment, niveau ou zone absent du site.</summary>
internal sealed class ElementIntrouvableException() : Exception("Élément introuvable sur ce site.");

/// <summary>Nom déjà pris au même endroit (409 « nom_existant »), casse ignorée.</summary>
internal sealed class NomExistantException() : Exception("Ce nom est déjà utilisé à cet endroit.");

/// <summary>Élément modifié par quelqu'un d'autre pendant la requête (409 « modifie_entre_temps ») : relire puis refaire.</summary>
internal sealed class ModifieEntreTempsException() : Exception("Cet élément vient d'être modifié par quelqu'un d'autre : rechargez puis recommencez.");

/// <summary>
/// Structure du patrimoine d'un site (US-07, S07-01) : bâtiments, niveaux, zones. Lecture pour les comptes internes du
/// site, création et renommage pour technicien, responsable et délégué (ModifierInventaire), tracés au journal.
/// </summary>
internal sealed class ServiceStructure(PatrimoineDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal async Task<IReadOnlyList<BatimentLu>> LireAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var batiments = await context.Batiments.AsNoTracking().Where(b => b.SiteId == siteId).OrderBy(b => b.Nom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var niveaux = await context.Niveaux.AsNoTracking().Where(n => n.SiteId == siteId).OrderBy(n => n.Ordre).ThenBy(n => n.Nom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var zones = await context.Zones.AsNoTracking().Where(z => z.SiteId == siteId).OrderBy(z => z.Nom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var niveauxParBatiment = niveaux.ToLookup(n => n.BatimentId);
        var zonesParBatiment = zones.ToLookup(z => z.BatimentId);
        return [.. batiments.Select(b => new BatimentLu(
            b.Id, b.Nom,
            [.. niveauxParBatiment[b.Id].Select(n => new NiveauLu(n.Id, n.Nom, n.Ordre))],
            [.. zonesParBatiment[b.Id].OrderBy(z => z.PartiesCommunes).Select(z => new ZoneLue(z.Id, z.Nom, z.NiveauId, z.PartiesCommunes))]))];
    }

    /// <summary>Bâtiment et sa zone « Parties communes » (DEC-01), dans la même transaction.</summary>
    internal async Task<Guid> CreerBatimentAsync(Guid organisationId, Guid siteId, string? nom, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var batiment = new Batiment(Guid.CreateVersion7(maintenant), organisationId, siteId, nom, maintenant);
        await ExigerNomLibreAsync(context.Batiments.Where(b => b.SiteId == siteId).Select(b => b.Nom), batiment.Nom, cancellationToken).ConfigureAwait(false);
        var communes = Zone.PartiesCommunesDe(Guid.CreateVersion7(maintenant), batiment);
        context.Batiments.Add(batiment);
        context.Zones.Add(communes);
        Tracer(siteId, CibleInventaire.Batiment, batiment.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = batiment.Nom });
        Tracer(siteId, CibleInventaire.Zone, communes.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = communes.Nom, batiment = batiment.Nom });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return batiment.Id;
    }

    internal async Task<Guid> CreerNiveauAsync(Guid organisationId, Guid siteId, Guid batimentId, string? nom, int ordre, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var batiment = await BatimentAsync(siteId, batimentId, cancellationToken).ConfigureAwait(false);
        var niveau = new Niveau(Guid.CreateVersion7(horloge.GetUtcNow()), batiment, nom, ordre);
        await ExigerNomLibreAsync(context.Niveaux.Where(n => n.BatimentId == batiment.Id).Select(n => n.Nom), niveau.Nom, cancellationToken).ConfigureAwait(false);
        context.Niveaux.Add(niveau);
        Tracer(siteId, CibleInventaire.Niveau, niveau.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = niveau.Nom, ordre, batiment = batiment.Nom });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return niveau.Id;
    }

    internal async Task<Guid> CreerZoneAsync(
        Guid organisationId, Guid siteId, Guid batimentId, Guid? niveauId, string? nom, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var batiment = await BatimentAsync(siteId, batimentId, cancellationToken).ConfigureAwait(false);
        var niveau = niveauId is { } id
            ? await context.Niveaux.SingleOrDefaultAsync(n => n.Id == id && n.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                ?? throw new ElementIntrouvableException()
            : null;
        var zone = new Zone(Guid.CreateVersion7(horloge.GetUtcNow()), batiment, niveau, nom);
        await ExigerNomLibreAsync(
            context.Zones.Where(z => z.BatimentId == batiment.Id && z.NiveauId == zone.NiveauId).Select(z => z.Nom), zone.Nom, cancellationToken).ConfigureAwait(false);
        context.Zones.Add(zone);
        Tracer(siteId, CibleInventaire.Zone, zone.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = zone.Nom, batiment = batiment.Nom, niveau = niveau?.Nom });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return zone.Id;
    }

    internal async Task RenommerAsync(Guid organisationId, Guid siteId, CibleInventaire cible, Guid id, string? nom, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        string avant;
        string apres;
        switch (cible)
        {
            case CibleInventaire.Batiment:
                var batiment = await BatimentAsync(siteId, id, cancellationToken).ConfigureAwait(false);
                avant = batiment.Nom;
                batiment.Renommer(nom);
                apres = batiment.Nom;
                await ExigerNomLibreAsync(context.Batiments.Where(b => b.SiteId == siteId && b.Id != id).Select(b => b.Nom), apres, cancellationToken).ConfigureAwait(false);
                break;
            case CibleInventaire.Niveau:
                var niveau = await context.Niveaux.SingleOrDefaultAsync(n => n.Id == id && n.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                    ?? throw new ElementIntrouvableException();
                avant = niveau.Nom;
                niveau.Renommer(nom);
                apres = niveau.Nom;
                await ExigerNomLibreAsync(
                    context.Niveaux.Where(n => n.BatimentId == niveau.BatimentId && n.Id != id).Select(n => n.Nom), apres, cancellationToken).ConfigureAwait(false);
                break;
            case CibleInventaire.Zone:
                var zone = await context.Zones.SingleOrDefaultAsync(z => z.Id == id && z.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                    ?? throw new ElementIntrouvableException();
                avant = zone.Nom;
                zone.Renommer(nom);
                apres = zone.Nom;
                await ExigerNomLibreAsync(
                    context.Zones.Where(z => z.BatimentId == zone.BatimentId && z.NiveauId == zone.NiveauId && z.Id != id).Select(z => z.Nom), apres, cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cible), cible, "Seuls bâtiments, niveaux et zones se renomment ici.");
        }

        if (avant == apres)
        {
            return; // sans changement : rien n'est écrit
        }

        Tracer(siteId, cible, id, NatureEvenementInventaire.Modification, new { nom = avant }, new { nom = apres });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    // Casse ignorée : « Église » et « église » désignent le même bâtiment. Les index en base couvrent l'écriture parallèle exacte.
    private static async Task ExigerNomLibreAsync(IQueryable<string> nomsVoisins, string nom, CancellationToken cancellationToken)
    {
        var motif = Motifs.Exact(nom);
        if (await nomsVoisins.AnyAsync(n => EF.Functions.ILike(n, motif, Motifs.Echappement), cancellationToken).ConfigureAwait(false))
        {
            throw new NomExistantException();
        }
    }

    private void Tracer(Guid siteId, CibleInventaire cible, Guid cibleId, NatureEvenementInventaire nature, object? avant, object? apres) =>
        context.Tracer(compte.Id, horloge.GetUtcNow(), siteId, cible, cibleId, nature, avant, apres);

    private async Task<Batiment> BatimentAsync(Guid siteId, Guid batimentId, CancellationToken cancellationToken) =>
        await context.Batiments.SingleOrDefaultAsync(b => b.Id == batimentId && b.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();
}
