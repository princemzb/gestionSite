using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

/// <summary>Nom déjà pris au même endroit (409 « nom_existant »).</summary>
internal sealed class NomExistantException() : Exception("Ce nom est déjà utilisé à cet endroit.");

/// <summary>
/// Structure du patrimoine d'un site (US-07, S07-01) : bâtiments, niveaux, zones. Lecture pour les comptes internes du
/// site, création et renommage pour technicien, responsable et délégué (ModifierInventaire), tracés au journal.
/// </summary>
internal sealed class ServiceStructure(PatrimoineDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal async Task<IReadOnlyList<BatimentLu>> LireAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var batiments = await context.Batiments.AsNoTracking().Where(b => b.SiteId == siteId).OrderBy(b => b.Nom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var niveaux = await context.Niveaux.AsNoTracking().Where(n => n.SiteId == siteId).OrderBy(n => n.Ordre)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var zones = await context.Zones.AsNoTracking().Where(z => z.SiteId == siteId).OrderBy(z => z.Nom)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. batiments.Select(b => new BatimentLu(
            b.Id, b.Nom,
            [.. niveaux.Where(n => n.BatimentId == b.Id).Select(n => new NiveauLu(n.Id, n.Nom, n.Ordre))],
            [.. zones.Where(z => z.BatimentId == b.Id).OrderBy(z => z.PartiesCommunes).Select(z => new ZoneLue(z.Id, z.Nom, z.NiveauId, z.PartiesCommunes))]))];
    }

    /// <summary>Bâtiment et sa zone « Parties communes » (DEC-01), dans la même transaction.</summary>
    internal async Task<Guid> CreerBatimentAsync(Guid organisationId, Guid siteId, string? nom, CancellationToken cancellationToken)
    {
        await ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var batiment = new Batiment(Guid.CreateVersion7(maintenant), organisationId, siteId, nom, maintenant);
        var communes = Zone.PartiesCommunesDe(Guid.CreateVersion7(maintenant), batiment);
        context.Batiments.Add(batiment);
        context.Zones.Add(communes);
        Tracer(siteId, CibleInventaire.Batiment, batiment.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = batiment.Nom });
        Tracer(siteId, CibleInventaire.Zone, communes.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = communes.Nom, batiment = batiment.Nom });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return batiment.Id;
    }

    internal async Task<Guid> CreerNiveauAsync(Guid organisationId, Guid siteId, Guid batimentId, string? nom, int ordre, CancellationToken cancellationToken)
    {
        await ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var batiment = await BatimentAsync(siteId, batimentId, cancellationToken).ConfigureAwait(false);
        var niveau = new Niveau(Guid.CreateVersion7(horloge.GetUtcNow()), batiment, nom, ordre);
        context.Niveaux.Add(niveau);
        Tracer(siteId, CibleInventaire.Niveau, niveau.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = niveau.Nom, ordre, batiment = batiment.Nom });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return niveau.Id;
    }

    internal async Task<Guid> CreerZoneAsync(
        Guid organisationId, Guid siteId, Guid batimentId, Guid? niveauId, string? nom, CancellationToken cancellationToken)
    {
        await ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var batiment = await BatimentAsync(siteId, batimentId, cancellationToken).ConfigureAwait(false);
        var niveau = niveauId is { } id
            ? await context.Niveaux.SingleOrDefaultAsync(n => n.Id == id && n.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                ?? throw new ElementIntrouvableException()
            : null;
        var zone = new Zone(Guid.CreateVersion7(horloge.GetUtcNow()), batiment, niveau, nom);
        context.Zones.Add(zone);
        Tracer(siteId, CibleInventaire.Zone, zone.Id, NatureEvenementInventaire.Creation, avant: null, new { nom = zone.Nom, batiment = batiment.Nom, niveau = niveau?.Nom });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return zone.Id;
    }

    internal async Task RenommerAsync(Guid organisationId, Guid siteId, CibleInventaire cible, Guid id, string? nom, CancellationToken cancellationToken)
    {
        await ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        string avant;
        string apres;
        switch (cible)
        {
            case CibleInventaire.Batiment:
                var batiment = await BatimentAsync(siteId, id, cancellationToken).ConfigureAwait(false);
                avant = batiment.Nom;
                batiment.Renommer(nom);
                apres = batiment.Nom;
                break;
            case CibleInventaire.Niveau:
                var niveau = await context.Niveaux.SingleOrDefaultAsync(n => n.Id == id && n.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                    ?? throw new ElementIntrouvableException();
                avant = niveau.Nom;
                niveau.Renommer(nom);
                apres = niveau.Nom;
                break;
            default:
                var zone = await context.Zones.SingleOrDefaultAsync(z => z.Id == id && z.SiteId == siteId, cancellationToken).ConfigureAwait(false)
                    ?? throw new ElementIntrouvableException();
                avant = zone.Nom;
                zone.Renommer(nom);
                apres = zone.Nom;
                break;
        }

        if (avant == apres)
        {
            return; // sans changement : rien n'est écrit
        }

        Tracer(siteId, cible, id, NatureEvenementInventaire.Modification, new { nom = avant }, new { nom = apres });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Batiment> BatimentAsync(Guid siteId, Guid batimentId, CancellationToken cancellationToken) =>
        await context.Batiments.SingleOrDefaultAsync(b => b.Id == batimentId && b.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();

    private void Tracer(Guid siteId, CibleInventaire cible, Guid cibleId, NatureEvenementInventaire nature, object? avant, object? apres)
    {
        var maintenant = horloge.GetUtcNow();
        context.Journal.Add(new EvenementInventaire(
            Guid.CreateVersion7(maintenant), context.OrganisationCourante, siteId, cible, cibleId, nature, compte.Id, maintenant,
            avant is null ? null : JsonSerializer.Serialize(avant), apres is null ? null : JsonSerializer.Serialize(apres)));
    }

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new NomExistantException();
        }
    }

    // Avant toute lecture : une décision favorable fixe l'organisation courante (filtres et RLS).
    private async Task ExigerAsync(Guid organisationId, Guid siteId, Droit droit, CancellationToken cancellationToken)
    {
        var decision = await autorisations.VerifierAsync(organisationId, droit, siteId, cancellationToken).ConfigureAwait(false);
        if (decision != DecisionAutorisation.Autorise)
        {
            throw new AccesInventaireRefuseException(decision);
        }
    }
}
