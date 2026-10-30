using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.DepotPublic;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Persistence;
using GestionSite.SharedKernel.Photos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Dossiers;

internal sealed record DossierResume(
    Guid Id, string Titre, string Statut, string? Priorite, Guid? ZoneId, string? Zone, bool Danger, bool ActiviteBloquee, string Origine,
    DateTimeOffset RecuLe, DateTimeOffset? EcheancePriseEnCharge = null, DateTimeOffset? EcheanceResolution = null);

internal sealed record InterventionLue(
    int Numero, string Titre, string Statut, Guid? IntervenantCompteId, string? Intervenant, string? Prestataire, Guid? ReferentCompteId,
    string? Referent, DateTimeOffset CreeLe, DateTimeOffset? DemarreeLe, string? MotifAttente, string? CommentaireAttente, DateOnly? ReexamenLe,
    string? Actions, string? EtatFinal, DateTimeOffset? ClotureeLe, IReadOnlyList<int> Photos, bool PhotoExigee);

internal sealed record EvenementDossierLu(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, JsonElement? Detail);

internal sealed record DossierLu(
    Guid Id, string Titre, string Statut, string Nature, string Description, string Emplacement, bool Danger, bool ActiviteBloquee,
    string Origine, DateTimeOffset RecuLe, string NomDeclarant, string? ContactDeclarant, IReadOnlyList<int> Photos, string? Priorite,
    LieuInventaire? Lieu, DateTimeOffset? QualifieLe, Guid? DoublonDe, string? MotifClassement, string? CommentaireClassement,
    DateTimeOffset? AffecteLe, DateTimeOffset? EcheancePriseEnCharge, DateTimeOffset? EcheanceResolution, DateTimeOffset? AttenteDepuis,
    DateOnly? ReexamenLe, long DureeAttenteSecondes, DateTimeOffset? ClotureLe, IReadOnlyList<InterventionLue> Interventions,
    IReadOnlyList<DossierResume> Doublons, IReadOnlyList<DossierResume> Suggestions, IReadOnlyList<EvenementDossierLu> Historique, uint Version);

/// <summary>Dossier absent du site (404).</summary>
internal sealed class DossierIntrouvableException() : Exception("Dossier introuvable sur ce site.");

/// <summary>Dossier modifié par quelqu'un d'autre depuis sa lecture (409 « modifie_entre_temps ») : relire puis refaire.</summary>
internal sealed class DossierModifieException() : Exception("Ce dossier vient d'être modifié par quelqu'un d'autre : rechargez puis recommencez.");

/// <summary>
/// Dossiers d'un site (US-09, S09-01) : liste et lecture pour les comptes internes, déclaration interne, qualification
/// (priorité, zone et composant), rattachement à un doublon et classement sans suite par R/D (RG-04, DEC-03, DEC-05).
/// Chaque décision est tracée au journal du dossier.
/// </summary>
internal sealed class ServiceDossiers(
    MaintenanceDbContext context, IAutorisations autorisations, IInventaireSite inventaire, IAnnuaireMembres annuaire, IStockageFichiers stockage,
    CompteCourant compte, TimeProvider horloge)
{
    internal const int Limite = 500;
    private const int TitreMax = 80;

    internal async Task<IReadOnlyList<DossierResume>> ListerAsync(Guid organisationId, Guid siteId, bool clos, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var dossiers = await context.Signalements.AsNoTracking()
            .Where(s => s.SiteId == siteId && Signalement.StatutsOuverts.Contains(s.Statut) != clos)
            .OrderByDescending(s => s.Id).Take(Limite)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ResumesAsync(siteId, dossiers, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<DossierLu> LireAsync(Guid organisationId, Guid siteId, Guid id, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        await autorisations.ExigerAsync(organisationId, siteId, Droit.LireContactsDeclarant, cancellationToken).ConfigureAwait(false);
        var lu = await context.Signalements.AsNoTracking().Where(s => s.Id == id && s.SiteId == siteId)
            .Select(s => new { Dossier = s, Version = EF.Property<uint>(s, "xmin") })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new DossierIntrouvableException();
        var dossier = lu.Dossier;
        var photos = await context.Photos.AsNoTracking().Where(p => p.SignalementId == id).OrderBy(p => p.Rang).Select(p => p.Rang)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var doublons = await context.Signalements.AsNoTracking().Where(s => s.SiteId == siteId && s.DoublonDe == id).OrderBy(s => s.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var suggestions = dossier.Statut == StatutDossier.AQualifier
            ? await SuggestionsAsync(dossier, cancellationToken).ConfigureAwait(false)
            : [];
        var journal = await context.Journal.AsNoTracking().Where(e => e.DossierId == id).OrderByDescending(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var interventions = await context.Interventions.AsNoTracking().Where(i => i.DossierId == id).OrderBy(i => i.Numero)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var idsInterventions = interventions.Select(i => i.Id).ToList();
        var photosInterventions = (await context.PhotosIntervention.AsNoTracking().Where(p => idsInterventions.Contains(p.InterventionId))
            .OrderBy(p => p.Rang).Select(p => new { p.InterventionId, p.Rang }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(p => p.InterventionId, p => p.Rang);
        var comptes = journal.Select(e => e.AuteurCompteId).Concat(interventions.SelectMany(i => new[] { i.IntervenantCompteId, i.ReferentCompteId }))
            .Where(c => c is not null).Select(c => c!.Value).Distinct().ToList();
        var auteurs = await annuaire.NomsAsync(comptes, cancellationToken).ConfigureAwait(false);
        var lieu = dossier.ZoneId is { } zone
            ? await inventaire.LieuAsync(siteId, zone, dossier.ComposantId, composantActif: false, cancellationToken).ConfigureAwait(false)
            : null;
        // Dépôts antérieurs au journal des dossiers (US-08) : leur déclaration est reconstituée depuis la date de réception.
        var historique = journal.Select(e => new EvenementDossierLu(
            e.Le, e.Nature.ToString(), e.AuteurCompteId, e.AuteurCompteId is { } auteur ? auteurs.GetValueOrDefault(auteur) : null, Json(e.Detail))).ToList();
        if (journal.All(e => e.Nature != NatureEvenementDossier.Declaration))
        {
            historique.Add(new EvenementDossierLu(dossier.RecuLe, nameof(NatureEvenementDossier.Declaration), null, null, null));
        }

        return new DossierLu(
            dossier.Id, Titre(dossier), dossier.Statut.ToString(), dossier.Nature.ToString(), dossier.Description, dossier.Emplacement, dossier.Danger,
            dossier.ActiviteBloquee, dossier.Origine.ToString(), dossier.RecuLe, dossier.NomDeclarant, dossier.ContactDeclarant, photos,
            dossier.Priorite?.ToString(), lieu, dossier.QualifieLe, dossier.DoublonDe, dossier.MotifClassement?.ToString(), dossier.CommentaireClassement,
            dossier.AffecteLe, dossier.EcheancePriseEnCharge, dossier.EcheanceResolution, dossier.AttenteDepuis,
            // Réexamen : le plus proche des interventions en attente (déduit, jamais recopié sur le dossier).
            interventions.Where(i => i.ReexamenLe is not null).Min(i => i.ReexamenLe), (long)dossier.DureeAttente.TotalSeconds, dossier.ClotureLe,
            [.. interventions.Select(i => new InterventionLue(
                i.Numero, i.Titre, i.Statut.ToString(), i.IntervenantCompteId, i.IntervenantCompteId is { } a ? auteurs.GetValueOrDefault(a) : null, i.Prestataire,
                i.ReferentCompteId, i.ReferentCompteId is { } r ? auteurs.GetValueOrDefault(r) : null, i.CreeLe, i.DemarreeLe,
                i.MotifAttente?.ToString(), i.CommentaireAttente, i.ReexamenLe, i.Actions, i.EtatFinal?.ToString(), i.ClotureeLe,
                [.. photosInterventions[i.Id]], i.PhotoExigee ?? PhotoExigee.Pour(dossier, lieu)))],
            await ResumesAsync(siteId, doublons, cancellationToken).ConfigureAwait(false),
            await ResumesAsync(siteId, suggestions, cancellationToken).ConfigureAwait(false),
            historique,
            lu.Version);
    }

    /// <summary>Déclaration d'un compte interne du site (décision du 29/10) ; même commande rejouée : même dossier.</summary>
    internal async Task<Guid> DeclarerAsync(
        Guid organisationId, Guid siteId, DemandeDeclaration demande, IReadOnlyList<PhotoRecue> photos, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DeposerSignalement, cancellationToken).ConfigureAwait(false);
        var zoneId = demande.ZoneId ?? throw new SaisieInvalideException("zoneId", "Zone obligatoire.");
        var lieu = await inventaire.LieuAsync(siteId, zoneId, composantId: null, composantActif: true, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException("zoneId", "Zone inconnue sur ce site.");
        var compteId = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        // Nom affiché du membre ; à défaut (compte sans nom), un libellé neutre : le compte reste la référence (DeclarantCompteId).
        var nom = (await annuaire.NomsAsync([compteId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(compteId) is { Length: > 0 } nomMembre
            ? nomMembre
            : "Compte interne";
        var maintenant = horloge.GetUtcNow();
        var dossier = Signalement.Interne(
            Guid.CreateVersion7(maintenant), organisationId, siteId, demande.CommandeId, Saisie.Valeur<NatureSignalement>(demande.Nature ?? "Incident", "nature"),
            demande.Description, demande.Emplacement, lieu.Libelle, lieu.ZoneId, demande.Danger, demande.ActiviteBloquee, compteId, nom,
            maintenant);
        if (await DejaDeclareAsync(dossier, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja;
        }

        // Photos (décision du 29/10) : préparées puis écrites avant l'enregistrement, sous des clés propres à ce dossier ;
        // retirées si l'enregistrement n'aboutit pas.
        var jpegs = photos.Select(p => TraitementPhotos.Preparer(p.Contenu)).ToList();
        await using var envoi = await PhotosStockees.EcrireAsync(
            stockage, jpegs, (rang, jpeg) => new PhotoSignalement(Guid.CreateVersion7(maintenant), dossier, rang, jpeg.Length), p => p.Cle,
            photos => { var id = photos[0].Id; return context.Photos.AsNoTracking().AnyAsync(p => p.Id == id); }, cancellationToken)
            .ConfigureAwait(false);
        context.Photos.AddRange(envoi.Photos);
        context.Signalements.Add(dossier);
        if (dossier.Danger)
        {
            context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(maintenant), dossier));
        }

        Tracer(dossier, NatureEvenementDossier.Declaration, maintenant, detail: null);
        try
        {
            await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
            envoi.Confirmer();
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Même envoi reçu en parallèle : l'autre requête l'a enregistré ; même réponse que pour un rejeu.
            context.ChangeTracker.Clear();
            return await DejaDeclareAsync(dossier, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Doublon signalé par la base mais introuvable.", doublon);
        }

        return dossier.Id;
    }

    // Rejeu : même compte, même site, même contenu → même dossier ; sinon la commande est réutilisée à tort (409).
    private async Task<Guid?> DejaDeclareAsync(Signalement envoye, CancellationToken cancellationToken)
    {
        var recu = await context.Signalements.AsNoTracking().Where(s => s.CommandeId == envoye.CommandeId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return recu switch
        {
            null => null,
            _ when recu.DeclarantCompteId == envoye.DeclarantCompteId && recu.ZoneId == envoye.ZoneId && recu.MemeEnvoi(envoye) => recu.Id,
            _ => throw new CommandeReutiliseeException(),
        };
    }

    /// <summary>Priorité (DEC-05), zone obligatoire et composant facultatif (décision du 29/10), à la version lue.</summary>
    internal async Task QualifierAsync(Guid organisationId, Guid siteId, Guid id, DemandeQualification demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var priorite = Saisie.Valeur<PrioriteDossier>(demande.Priorite, "priorite");
        var zoneId = demande.ZoneId ?? throw new SaisieInvalideException("zoneId", "Zone obligatoire.");
        var lieu = await inventaire.LieuAsync(siteId, zoneId, demande.ComposantId, composantActif: true, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException(demande.ComposantId is null ? "zoneId" : "composantId", "Zone ou composant inconnu sur ce site.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var dossier = await context.DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        if (!dossier.Qualifier(priorite, lieu.ZoneId, lieu.ComposantId, maintenant))
        {
            return; // sans changement : rien n'est écrit
        }

        Tracer(dossier, NatureEvenementDossier.Qualification, maintenant,
            new { priorite = priorite.ToString(), zone = lieu.Libelle, composant = lieu.Composant });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task RattacherAsync(Guid organisationId, Guid siteId, Guid id, DemandeRattachement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var principalId = demande.PrincipalId ?? throw new SaisieInvalideException("principalId", "Dossier principal obligatoire.");
        // Les deux dossiers verrouillés (ordre fixe) : un rattachement concurrent ne peut pas créer de chaîne ni d'orphelin.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await context.VerrouillerAsync([id, principalId], cancellationToken).ConfigureAwait(false);
        var dossier = await context.DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var principal = await context.Signalements.SingleOrDefaultAsync(s => s.Id == principalId && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException("principalId", "Dossier principal inconnu sur ce site.");
        dossier.RattacherA(principal, await ADesDoublonsAsync(id, cancellationToken).ConfigureAwait(false));
        Tracer(dossier, NatureEvenementDossier.RattachementDoublon, horloge.GetUtcNow(), new { principal = principal.Id, titre = Titre(principal) });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task ClasserAsync(Guid organisationId, Guid siteId, Guid id, DemandeClassement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var dossier = await context.DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        dossier.ClasserSansSuite(Saisie.Valeur<MotifClassement>(demande.Motif, "motif"), demande.Commentaire, await ADesDoublonsAsync(id, cancellationToken).ConfigureAwait(false));
        Tracer(dossier, NatureEvenementDossier.ClassementSansSuite, horloge.GetUtcNow(),
            new { motif = dossier.MotifClassement.ToString(), commentaire = dossier.CommentaireClassement });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Réouverture motivée par R/D d'un dossier clôturé ou classé (RG-06, DEC-03) : retour « À qualifier ».</summary>
    internal async Task RouvrirAsync(Guid organisationId, Guid siteId, Guid id, DemandeReouverture demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.RouvrirArbitrer, cancellationToken).ConfigureAwait(false);
        var motif = TexteAffichable.Nettoyer(demande.Motif, Signalement.CommentaireMax, multiligne: true)
            ?? throw new SaisieInvalideException("motif", $"Motif de réouverture : texte visible de {Signalement.CommentaireMax} caractères au plus.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var dossier = await context.DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var maintenant = horloge.GetUtcNow();
        dossier.Rouvrir(maintenant);
        Tracer(dossier, NatureEvenementDossier.Reouverture, maintenant, new { motif });
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Doublons possibles (DEC-03) : dossiers ouverts du même site, même zone si elle est connue, sinon même emplacement saisi.
    private async Task<List<Signalement>> SuggestionsAsync(Signalement dossier, CancellationToken cancellationToken)
    {
        var candidats = context.Signalements.AsNoTracking()
            .Where(s => s.SiteId == dossier.SiteId && s.Id != dossier.Id && s.DoublonDe == null && Signalement.StatutsOuverts.Contains(s.Statut));
        // Même emplacement, casse ignorée, saisie littérale (% et _ échappés).
        var emplacement = Motifs.Exact(dossier.Emplacement);
        candidats = dossier.ZoneId is { } zone
            ? candidats.Where(s => s.ZoneId == zone || EF.Functions.ILike(s.Emplacement, emplacement, Motifs.Echappement))
            : candidats.Where(s => EF.Functions.ILike(s.Emplacement, emplacement, Motifs.Echappement));
        return await candidats.OrderByDescending(s => s.Id).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DossierResume>> ResumesAsync(Guid siteId, List<Signalement> dossiers, CancellationToken cancellationToken)
    {
        var zones = await inventaire.ZonesAsync(siteId, [.. dossiers.Where(d => d.ZoneId is not null).Select(d => d.ZoneId!.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false);
        return [.. dossiers.Select(d => new DossierResume(
            d.Id, Titre(d), d.Statut.ToString(), d.Priorite?.ToString(), d.ZoneId, d.ZoneId is { } z ? zones.GetValueOrDefault(z) : null, d.Danger,
            d.ActiviteBloquee, d.Origine.ToString(), d.RecuLe, d.EcheancePriseEnCharge, d.EcheanceResolution))];
    }

    private Task<bool> ADesDoublonsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Signalements.AnyAsync(s => s.DoublonDe == id, cancellationToken);

    private void Tracer(Signalement dossier, NatureEvenementDossier nature, DateTimeOffset le, object? detail) =>
        context.Tracer(dossier, nature, compte.Id, le, detail);

    // Titre d'affichage : première ligne de la description, raccourcie.
    internal static string Titre(Signalement dossier)
    {
        var ligne = dossier.Description.Split('\n', 2)[0].Trim();
        return ligne.Length <= TitreMax ? ligne : string.Concat(ligne.AsSpan(0, TitreMax - 1), "…");
    }

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

internal static class Saisie
{
    internal static T Valeur<T>(string? texte, string champ) where T : struct, Enum =>
        NomExact.Essayer<T>(texte, out var valeur) ? valeur : throw new SaisieInvalideException(champ, $"{champ} : valeur inconnue.");
}
