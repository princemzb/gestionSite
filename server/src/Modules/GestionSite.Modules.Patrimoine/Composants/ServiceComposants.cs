using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Patrimoine.Composants;

internal sealed record RepartitionLue(int Bon, int Satisfaisant, int Degrade, int Mauvais, int Critique);

internal sealed record LotLu(string Unite, int Quantite, RepartitionLue? Repartition);

internal sealed record ComposantLu(
    Guid Id, Guid ZoneId, string Nom, string CorpsEtat, string? Emplacement, string? Marque, string? Modele, string? NumeroSerie,
    int? AnneeMiseEnService, int? MoisMiseEnService, string? Remarques, bool SoumisAVerification, LotLu? Lot,
    DateTimeOffset? RetireLe, string? MotifRetrait, uint Version);

internal sealed record ListeComposants(IReadOnlyList<ComposantLu> Composants, bool Tronquee);

internal sealed record EvenementLu(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, JsonElement? Avant, JsonElement? Apres);

internal sealed record FicheLue(ComposantLu Composant, IReadOnlyList<EvenementLu> Historique);

internal sealed record FiltreComposants(Guid? ZoneId, Guid? BatimentId, CorpsEtat? CorpsEtat, bool? Verification, string? Recherche, bool Retires);

/// <summary>
/// Composants de l'inventaire d'un site (US-07, S07-02) : individuels ou en lots, modifiés à version connue, retirés sans
/// suppression. Chaque création, modification (champs changés, avant et après) et retrait est tracé au journal.
/// </summary>
internal sealed class ServiceComposants(PatrimoineDbContext context, IAutorisations autorisations, CompteCourant compte, TimeProvider horloge)
{
    internal const int Limite = 1000;

    internal async Task<ListeComposants> ListerAsync(Guid organisationId, Guid siteId, FiltreComposants filtre, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var requete = context.Composants.AsNoTracking().Where(c => c.SiteId == siteId);
        if (!filtre.Retires)
        {
            requete = requete.Where(c => c.RetireLe == null);
        }

        if (filtre.ZoneId is { } zoneId)
        {
            requete = requete.Where(c => c.ZoneId == zoneId);
        }

        if (filtre.BatimentId is { } batimentId)
        {
            requete = requete.Where(c => context.Zones.Any(z => z.Id == c.ZoneId && z.BatimentId == batimentId));
        }

        if (filtre.CorpsEtat is { } corpsEtat)
        {
            requete = requete.Where(c => c.CorpsEtat == corpsEtat);
        }

        if (filtre.Verification is { } verification)
        {
            requete = requete.Where(c => c.SoumisAVerification == verification);
        }

        if (filtre.Recherche is not null)
        {
            var motif = Motifs.Contenant(TexteAffichable.Nettoyer(filtre.Recherche, Nom.Max)
                ?? throw new SaisieInvalideException("recherche", $"recherche : une ligne visible de {Nom.Max} caractères au plus."));
            requete = requete.Where(c => EF.Functions.ILike(c.Nom, motif, Motifs.Echappement)
                || EF.Functions.ILike(c.Marque!, motif, Motifs.Echappement) || EF.Functions.ILike(c.Modele!, motif, Motifs.Echappement)
                || EF.Functions.ILike(c.NumeroSerie!, motif, Motifs.Echappement) || EF.Functions.ILike(c.Emplacement!, motif, Motifs.Echappement));
        }

        var lus = await requete.OrderBy(c => c.Nom).ThenBy(c => c.Id).Take(Limite + 1)
            .Select(c => new { Composant = c, Version = EF.Property<uint>(c, "xmin") })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ListeComposants([.. lus.Take(Limite).Select(l => Lire(l.Composant, l.Version))], lus.Count > Limite);
    }

    internal async Task<FicheLue> LireAsync(Guid organisationId, Guid siteId, Guid id, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var composant = await ComposantAsync(siteId, id, cancellationToken).ConfigureAwait(false);
        var historique = await context.Journal.AsNoTracking()
            .Where(e => e.SiteId == siteId && e.Cible == CibleInventaire.Composant && e.CibleId == id).OrderByDescending(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new FicheLue(
            Lire(composant, Version(composant)),
            [.. historique.Select(e => new EvenementLu(e.Le, e.Nature.ToString(), e.AuteurCompteId, Json(e.Avant), Json(e.Apres)))]);
    }

    internal async Task<Guid> CreerAsync(Guid organisationId, Guid siteId, Guid zoneId, FicheComposant fiche, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var zone = await ZoneAsync(siteId, zoneId, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        var composant = new Composant(Guid.CreateVersion7(maintenant), zone, fiche, maintenant);
        context.Composants.Add(composant);
        var apres = Releve(composant, zone).Where(champ => champ.Value is not null).ToDictionary();
        context.Tracer(compte.Id, maintenant, siteId, CibleInventaire.Composant, composant.Id, NatureEvenementInventaire.Creation, avant: null, apres);
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return composant.Id;
    }

    /// <summary>Remplace la fiche lue à <paramref name="version"/> ; une version dépassée est refusée (409), sans rien écrire.</summary>
    internal async Task ModifierAsync(
        Guid organisationId, Guid siteId, Guid id, Guid zoneId, FicheComposant fiche, uint version, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var composant = await ComposantAVersionAsync(siteId, id, version, cancellationToken).ConfigureAwait(false);
        var zoneAvant = await context.Zones.SingleAsync(z => z.Id == composant.ZoneId, cancellationToken).ConfigureAwait(false);
        var zone = zoneId == zoneAvant.Id ? zoneAvant : await ZoneAsync(siteId, zoneId, cancellationToken).ConfigureAwait(false);
        var avant = Releve(composant, zoneAvant);
        var maintenant = horloge.GetUtcNow();
        composant.Modifier(zone, fiche, maintenant);
        var apres = Releve(composant, zone);
        var changes = avant.Keys.Where(champ => !Equals(avant[champ], apres[champ])).ToList();
        if (changes.Count == 0)
        {
            return; // sans changement : rien n'est écrit
        }

        context.Tracer(compte.Id, maintenant, siteId, CibleInventaire.Composant, id, NatureEvenementInventaire.Modification,
            changes.ToDictionary(champ => champ, champ => avant[champ]), changes.ToDictionary(champ => champ, champ => apres[champ]));
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task RetirerAsync(Guid organisationId, Guid siteId, Guid id, string? motif, uint version, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ModifierInventaire, cancellationToken).ConfigureAwait(false);
        var composant = await ComposantAVersionAsync(siteId, id, version, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        composant.Retirer(motif, maintenant);
        context.Tracer(compte.Id, maintenant, siteId, CibleInventaire.Composant, id, NatureEvenementInventaire.Retrait,
            avant: null, new { motif = composant.MotifRetrait });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Composant> ComposantAsync(Guid siteId, Guid id, CancellationToken cancellationToken) =>
        await context.Composants.SingleOrDefaultAsync(c => c.Id == id && c.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();

    // Version lue par le formulaire : déjà dépassée, refus immédiat ; dépassée pendant la requête, refus à l'enregistrement (xmin).
    private async Task<Composant> ComposantAVersionAsync(Guid siteId, Guid id, uint version, CancellationToken cancellationToken)
    {
        var composant = await ComposantAsync(siteId, id, cancellationToken).ConfigureAwait(false);
        if (Version(composant) != version)
        {
            throw new ModifieEntreTempsException();
        }

        return composant;
    }

    // Zone désignée dans le corps : absente du site, c'est une saisie invalide, pas une ressource introuvable.
    private async Task<Zone> ZoneAsync(Guid siteId, Guid zoneId, CancellationToken cancellationToken) =>
        await context.Zones.SingleOrDefaultAsync(z => z.Id == zoneId && z.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException("zoneId", "Zone inconnue sur ce site.");

    private uint Version(Composant composant) => context.Entry(composant).Property<uint>("xmin").CurrentValue;

    // Valeurs comparées champ à champ pour le journal (types simples, énumérations en texte).
    private static Dictionary<string, object?> Releve(Composant composant, Zone zone) => new()
    {
        ["nom"] = composant.Nom,
        ["corpsEtat"] = composant.CorpsEtat.ToString(),
        ["zoneId"] = composant.ZoneId,
        ["zone"] = zone.Nom,
        ["emplacement"] = composant.Emplacement,
        ["marque"] = composant.Marque,
        ["modele"] = composant.Modele,
        ["numeroSerie"] = composant.NumeroSerie,
        ["anneeMiseEnService"] = composant.AnneeMiseEnService,
        ["moisMiseEnService"] = composant.MoisMiseEnService,
        ["remarques"] = composant.Remarques,
        ["soumisAVerification"] = composant.SoumisAVerification,
        ["unite"] = composant.Unite?.ToString(),
        ["quantite"] = composant.Quantite,
        ["bon"] = composant.QuantiteBon,
        ["satisfaisant"] = composant.QuantiteSatisfaisant,
        ["degrade"] = composant.QuantiteDegrade,
        ["mauvais"] = composant.QuantiteMauvais,
        ["critique"] = composant.QuantiteCritique,
    };

    private static ComposantLu Lire(Composant c, uint version) => new(
        c.Id, c.ZoneId, c.Nom, c.CorpsEtat.ToString(), c.Emplacement, c.Marque, c.Modele, c.NumeroSerie,
        c.AnneeMiseEnService, c.MoisMiseEnService, c.Remarques, c.SoumisAVerification,
        c.Lot is { } lot
            ? new LotLu(lot.Unite.ToString(), lot.Quantite, lot.Repartition is { } r ? new RepartitionLue(r.Bon, r.Satisfaisant, r.Degrade, r.Mauvais, r.Critique) : null)
            : null,
        c.RetireLe, c.MotifRetrait, version);

    private static JsonElement? Json(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
