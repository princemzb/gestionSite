using System.Text.Json;
using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.Modules.Patrimoine.Contracts;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.Dossiers;

internal sealed record DossierResume(
    Guid Id, string Titre, string Statut, string? Priorite, Guid? ZoneId, string? Zone, bool Danger, bool ActiviteBloquee, string Origine,
    DateTimeOffset RecuLe);

internal sealed record EvenementDossierLu(DateTimeOffset Le, string Nature, Guid? AuteurCompteId, string? Auteur, JsonElement? Detail);

internal sealed record DossierLu(
    Guid Id, string Titre, string Statut, string Nature, string Description, string Emplacement, bool Danger, bool ActiviteBloquee,
    string Origine, DateTimeOffset RecuLe, string NomDeclarant, string? ContactDeclarant, IReadOnlyList<int> Photos, string? Priorite,
    LieuInventaire? Lieu, DateTimeOffset? QualifieLe, Guid? DoublonDe, string? MotifClassement, string? CommentaireClassement,
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
    MaintenanceDbContext context, IAutorisations autorisations, IInventaireSite inventaire, IAnnuaireMembres annuaire, CompteCourant compte,
    TimeProvider horloge)
{
    internal const int Limite = 500;
    private const int TitreMax = 80;

    internal async Task<IReadOnlyList<DossierResume>> ListerAsync(Guid organisationId, Guid siteId, bool clos, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var ouverts = new[] { StatutDossier.AQualifier, StatutDossier.Affecte, StatutDossier.EnCours, StatutDossier.EnAttente };
        var dossiers = await context.Signalements.AsNoTracking()
            .Where(s => s.SiteId == siteId && ouverts.Contains(s.Statut) != clos)
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
        var auteurs = await annuaire.NomsAsync([.. journal.Where(e => e.AuteurCompteId is not null).Select(e => e.AuteurCompteId!.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false);
        var lieu = dossier.ZoneId is { } zone
            ? await inventaire.LieuAsync(siteId, zone, dossier.ComposantId, cancellationToken).ConfigureAwait(false)
            : null;
        return new DossierLu(
            dossier.Id, Titre(dossier), dossier.Statut.ToString(), dossier.Nature.ToString(), dossier.Description, dossier.Emplacement, dossier.Danger,
            dossier.ActiviteBloquee, dossier.Origine.ToString(), dossier.RecuLe, dossier.NomDeclarant, dossier.ContactDeclarant, photos,
            dossier.Priorite?.ToString(), lieu, dossier.QualifieLe, dossier.DoublonDe, dossier.MotifClassement?.ToString(), dossier.CommentaireClassement,
            await ResumesAsync(siteId, doublons, cancellationToken).ConfigureAwait(false),
            await ResumesAsync(siteId, suggestions, cancellationToken).ConfigureAwait(false),
            [.. journal.Select(e => new EvenementDossierLu(
                e.Le, e.Nature.ToString(), e.AuteurCompteId, e.AuteurCompteId is { } auteur ? auteurs.GetValueOrDefault(auteur) : null, Json(e.Detail)))],
            lu.Version);
    }

    /// <summary>Déclaration d'un compte interne du site (décision du 29/10) ; même commande rejouée : même dossier.</summary>
    internal async Task<Guid> DeclarerAsync(Guid organisationId, Guid siteId, DemandeDeclaration demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.DeposerSignalement, cancellationToken).ConfigureAwait(false);
        var zoneId = demande.ZoneId ?? throw new SaisieInvalideException("zoneId", "Zone obligatoire.");
        var lieu = await inventaire.LieuAsync(siteId, zoneId, composantId: null, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException("zoneId", "Zone inconnue sur ce site.");
        var compteId = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        // Nom affiché du membre ; à défaut (compte sans nom), un libellé neutre : le compte reste la référence (DeclarantCompteId).
        var nom = (await annuaire.NomsAsync([compteId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(compteId) is { Length: > 0 } nomMembre
            ? nomMembre
            : "Compte interne";
        if (await context.Signalements.AsNoTracking().Where(s => s.CommandeId == demande.CommandeId)
                .Select(s => new { s.Id, s.DeclarantCompteId }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja.DeclarantCompteId == compteId ? deja.Id : throw new CommandeDejaUtiliseeException();
        }

        var maintenant = horloge.GetUtcNow();
        var dossier = Signalement.Interne(
            Guid.CreateVersion7(maintenant), organisationId, siteId, demande.CommandeId, Saisie.Valeur<NatureSignalement>(demande.Nature ?? "Incident", "nature"),
            demande.Description, demande.Emplacement, $"{lieu.Batiment} · {lieu.Zone}", lieu.ZoneId, demande.Danger, demande.ActiviteBloquee, compteId, nom,
            maintenant);
        context.Signalements.Add(dossier);
        if (dossier.Danger)
        {
            context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(maintenant), dossier));
        }

        Tracer(dossier, NatureEvenementDossier.Declaration, maintenant, detail: null);
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        return dossier.Id;
    }

    /// <summary>Priorité (DEC-05), zone obligatoire et composant facultatif (décision du 29/10), à la version lue.</summary>
    internal async Task QualifierAsync(Guid organisationId, Guid siteId, Guid id, DemandeQualification demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var dossier = await DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var priorite = Saisie.Valeur<PrioriteDossier>(demande.Priorite, "priorite");
        var zoneId = demande.ZoneId ?? throw new SaisieInvalideException("zoneId", "Zone obligatoire.");
        var lieu = await inventaire.LieuAsync(siteId, zoneId, demande.ComposantId, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException(demande.ComposantId is null ? "zoneId" : "composantId", "Zone ou composant inconnu sur ce site.");
        var maintenant = horloge.GetUtcNow();
        dossier.Qualifier(priorite, lieu.ZoneId, lieu.ComposantId, maintenant);
        Tracer(dossier, NatureEvenementDossier.Qualification, maintenant,
            new { priorite = priorite.ToString(), zone = $"{lieu.Batiment} · {lieu.Zone}", composant = lieu.Composant });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task RattacherAsync(Guid organisationId, Guid siteId, Guid id, DemandeRattachement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var dossier = await DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        var principalId = demande.PrincipalId ?? throw new SaisieInvalideException("principalId", "Dossier principal obligatoire.");
        var principal = await context.Signalements.SingleOrDefaultAsync(s => s.Id == principalId && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new SaisieInvalideException("principalId", "Dossier principal inconnu sur ce site.");
        dossier.RattacherA(principal);
        Tracer(dossier, NatureEvenementDossier.RattachementDoublon, horloge.GetUtcNow(), new { principal = principal.Id, titre = Titre(principal) });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task ClasserAsync(Guid organisationId, Guid siteId, Guid id, DemandeClassement demande, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.QualifierAffecter, cancellationToken).ConfigureAwait(false);
        var dossier = await DossierAVersionAsync(siteId, id, demande.Version, cancellationToken).ConfigureAwait(false);
        dossier.ClasserSansSuite(Saisie.Valeur<MotifClassement>(demande.Motif, "motif"), demande.Commentaire);
        Tracer(dossier, NatureEvenementDossier.ClassementSansSuite, horloge.GetUtcNow(),
            new { motif = dossier.MotifClassement.ToString(), commentaire = dossier.CommentaireClassement });
        await EnregistrerAsync(cancellationToken).ConfigureAwait(false);
    }

    // Doublons possibles (DEC-03) : dossiers ouverts du même site, même zone si elle est connue, sinon même emplacement saisi.
    private async Task<List<Signalement>> SuggestionsAsync(Signalement dossier, CancellationToken cancellationToken)
    {
        var ouverts = new[] { StatutDossier.AQualifier, StatutDossier.Affecte, StatutDossier.EnCours, StatutDossier.EnAttente };
        var candidats = context.Signalements.AsNoTracking()
            .Where(s => s.SiteId == dossier.SiteId && s.Id != dossier.Id && s.DoublonDe == null && ouverts.Contains(s.Statut));
        candidats = dossier.ZoneId is { } zone
            ? candidats.Where(s => s.ZoneId == zone || EF.Functions.ILike(s.Emplacement, dossier.Emplacement))
            : candidats.Where(s => EF.Functions.ILike(s.Emplacement, dossier.Emplacement));
        return await candidats.OrderByDescending(s => s.Id).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DossierResume>> ResumesAsync(Guid siteId, List<Signalement> dossiers, CancellationToken cancellationToken)
    {
        var zones = await inventaire.ZonesAsync(siteId, [.. dossiers.Where(d => d.ZoneId is not null).Select(d => d.ZoneId!.Value).Distinct()], cancellationToken)
            .ConfigureAwait(false);
        return [.. dossiers.Select(d => new DossierResume(
            d.Id, Titre(d), d.Statut.ToString(), d.Priorite?.ToString(), d.ZoneId, d.ZoneId is { } z ? zones.GetValueOrDefault(z) : null, d.Danger,
            d.ActiviteBloquee, d.Origine.ToString(), d.RecuLe))];
    }

    // Version lue par l'écran, posée comme valeur d'origine de xmin : dépassée, l'enregistrement est refusé (409).
    private async Task<Signalement> DossierAVersionAsync(Guid siteId, Guid id, uint? version, CancellationToken cancellationToken)
    {
        var dossier = await context.Signalements.SingleOrDefaultAsync(s => s.Id == id && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new DossierIntrouvableException();
        context.Entry(dossier).Property<uint>("xmin").OriginalValue = version ?? throw new SaisieInvalideException("version", "Version du dossier lue obligatoire.");
        return dossier;
    }

    private void Tracer(Signalement dossier, NatureEvenementDossier nature, DateTimeOffset le, object? detail) =>
        context.Journal.Add(new EvenementDossier(
            Guid.CreateVersion7(le), dossier, nature, compte.Id, le, detail is null ? null : JsonSerializer.Serialize(detail)));

    private async Task EnregistrerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DossierModifieException();
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new CommandeDejaUtiliseeException(); // même commande enregistrée en parallèle
        }
    }

    // Titre d'affichage : première ligne de la description, raccourcie.
    private static string Titre(Signalement dossier)
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

/// <summary>Commande de déclaration déjà utilisée par un autre envoi (409 « commande_reutilisee »).</summary>
internal sealed class CommandeDejaUtiliseeException() : Exception("Cet identifiant d'envoi a déjà servi pour une autre déclaration.");

internal static class Saisie
{
    internal static T Valeur<T>(string? texte, string champ) where T : struct, Enum =>
        NomExact.Essayer<T>(texte, out var valeur) ? valeur : throw new SaisieInvalideException(champ, $"{champ} : valeur inconnue.");
}
