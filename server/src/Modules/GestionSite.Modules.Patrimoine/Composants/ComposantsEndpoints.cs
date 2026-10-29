using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Structure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GestionSite.Modules.Patrimoine.Composants;

internal sealed record DemandeRepartition(int? Bon, int? Satisfaisant, int? Degrade, int? Mauvais, int? Critique);

internal sealed record DemandeLot(string? Unite, int? Quantite, DemandeRepartition? Repartition);

// Lot absent : composant individuel. Version : celle lue avec la fiche, exigée pour modifier.
internal sealed record DemandeComposant(
    string? Nom, string? CorpsEtat, Guid? ZoneId, string? Emplacement, string? Marque, string? Modele, string? NumeroSerie,
    int? AnneeMiseEnService, int? MoisMiseEnService, string? Remarques, bool? SoumisAVerification, DemandeLot? Lot, uint? Version);

internal sealed record DemandeRetrait(string? Motif, uint? Version);

/// <summary>Composants de l'inventaire d'un site (CONTRATS : inventaire, S07-02).</summary>
internal static class ComposantsEndpoints
{
    internal static IEndpointRouteBuilder MapComposants(this IEndpointRouteBuilder endpoints)
    {
        var composants = endpoints.MapGroup("/organizations/{orgId:guid}/sites/{siteId:guid}/inventory/components")
            .RequireAuthorization().AddEndpointFilter(StructureEndpoints.TraduireRefus);
        composants.MapGet("/", async (
            Guid orgId, Guid siteId, Guid? zoneId, Guid? batimentId, string? corpsEtat, bool? verification, string? recherche, bool? retires,
            ServiceComposants service, CancellationToken ct) =>
        {
            var filtre = new FiltreComposants(
                zoneId, batimentId, corpsEtat is null ? null : Valeur<CorpsEtat>(corpsEtat, "corpsEtat"), verification, recherche, retires ?? false);
            return Results.Ok(await service.ListerAsync(orgId, siteId, filtre, ct).ConfigureAwait(false));
        });
        composants.MapGet("/{id:guid}", async (Guid orgId, Guid siteId, Guid id, ServiceComposants service, CancellationToken ct) =>
            Results.Ok(await service.LireAsync(orgId, siteId, id, ct).ConfigureAwait(false)));
        composants.MapPost("/", async (Guid orgId, Guid siteId, DemandeComposant demande, ServiceComposants service, CancellationToken ct) =>
            Results.Ok(new Cree(await service.CreerAsync(orgId, siteId, Zone(demande), Fiche(demande, verificationExigee: false), ct).ConfigureAwait(false))));
        composants.MapPut("/{id:guid}", async (Guid orgId, Guid siteId, Guid id, DemandeComposant demande, ServiceComposants service, CancellationToken ct) =>
        {
            await service.ModifierAsync(orgId, siteId, id, Zone(demande), Fiche(demande, verificationExigee: true), Version(demande.Version), ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        });
        composants.MapPost("/{id:guid}/withdraw", async (Guid orgId, Guid siteId, Guid id, DemandeRetrait demande, ServiceComposants service, CancellationToken ct) =>
        {
            await service.RetirerAsync(orgId, siteId, id, demande.Motif, Version(demande.Version), ct).ConfigureAwait(false);
            return Results.NoContent();
        });
        return endpoints;
    }

    // À la création, « soumis à vérification » absent prend la proposition du corps d'état ; une modification le fixe toujours.
    private static FicheComposant Fiche(DemandeComposant demande, bool verificationExigee)
    {
        var corpsEtat = Valeur<CorpsEtat>(demande.CorpsEtat, "corpsEtat");
        var verification = demande.SoumisAVerification
            ?? (verificationExigee
                ? throw new SaisieInvalideException("soumisAVerification", "Indiquer si le composant est soumis à vérification.")
                : CorpsEtats.VerificationProposee(corpsEtat));
        return new FicheComposant(
            demande.Nom, corpsEtat, demande.Emplacement, demande.Marque, demande.Modele, demande.NumeroSerie,
            demande.AnneeMiseEnService, demande.MoisMiseEnService, demande.Remarques, verification, Lot(demande.Lot));
    }

    // Répartition : un état non saisi compte zéro ; la somme est contrôlée par le domaine.
    private static Lot? Lot(DemandeLot? lot) => lot is null
        ? null
        : new Lot(
            Valeur<UniteLot>(lot.Unite, "lot.unite"),
            lot.Quantite ?? throw new SaisieInvalideException("lot.quantite", "Quantité du lot obligatoire."),
            lot.Repartition is { } r ? new Repartition(r.Bon ?? 0, r.Satisfaisant ?? 0, r.Degrade ?? 0, r.Mauvais ?? 0, r.Critique ?? 0) : null);

    private static Guid Zone(DemandeComposant demande) => demande.ZoneId ?? throw new SaisieInvalideException("zoneId", "Zone obligatoire.");

    private static uint Version(uint? version) => version ?? throw new SaisieInvalideException("version", "Version de la fiche lue obligatoire.");

    // Nom exact seulement : Enum.TryParse accepterait « 6 » ou « Gaz, Autre ».
    private static T Valeur<T>(string? texte, string champ) where T : struct, Enum =>
        texte is not null && Enum.GetNames<T>().Contains(texte, StringComparer.Ordinal) && Enum.TryParse<T>(texte, out var valeur)
            ? valeur
            : throw new SaisieInvalideException(champ, $"{champ} : valeur inconnue.");
}
