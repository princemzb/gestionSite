using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Dossiers;
using GestionSite.Modules.Maintenance.Notifications;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace GestionSite.Modules.Maintenance.Dangers;

internal sealed record DemandePriseEnCompte(uint? Version);

internal sealed record DemandeMiseEnSecurite(string? Action, string? Commentaire, uint? Version);

internal sealed record DemandeVerification(bool? Suffisante, string? Motif, uint? Version);

internal sealed record DemandeFinDanger(string? Motif, string? Commentaire, uint? Version);

/// <summary>Mise en sécurité déclarée (en cours) d'un danger ; vérifiée si <c>verifieeLe</c> est renseignée.</summary>
internal sealed record MiseEnSecuriteLue(
    string Action, string? Commentaire, DateTimeOffset DeclareeLe, Guid DeclareeParCompteId, string? DeclareePar, DateTimeOffset? VerifieeLe,
    Guid? VerifieeParCompteId, string? VerifieePar);

/// <summary>Fin du suivi : levé ou écarté, motif, commentaire, auteur (null : alerte antérieure au suivi, close à la migration) et instant.</summary>
internal sealed record FinDangerLue(string Nature, string Motif, string? Commentaire, DateTimeOffset Le, Guid? ParCompteId, string? Par);

internal sealed record DangerLu(
    Guid Id, string Etat, DateTimeOffset CreeLe, Guid? ConstatId, DateTimeOffset? PrisEnCompteLe, Guid? PrisEnCompteParCompteId,
    string? PrisEnComptePar, MiseEnSecuriteLue? MiseEnSecurite, FinDangerLue? Fin, uint Version);

/// <summary>Danger en cours d'un site (bandeau du suivi des dossiers) et son dossier, ouvert ou clos.</summary>
internal sealed record DangerEnCours(Guid DossierId, string Titre, string StatutDossier, DangerLu Danger);

/// <summary>Danger absent du dossier, ou dossier absent du site (404).</summary>
internal sealed class DangerIntrouvableException() : Exception("Danger introuvable.");

/// <summary>
/// Suivi d'un danger (US-10, S10-04), parallèle au dossier : prise en compte par un R/D, mise en sécurité déclarée (T, R/D,
/// P/C, dès l'alerte ouverte) puis vérifiée par un R/D, danger levé ou écarté par un R/D. Chaque étape est tracée au journal
/// du dossier (auteur, instant, motif) ; les actions sur les dangers d'un même dossier sont sérialisées (verrou du dossier)
/// et faites à la version lue du danger (409 sinon).
/// </summary>
internal sealed class ServiceDangers(
    MaintenanceDbContext context, IAutorisations autorisations, IAnnuaireMembres annuaire, CompteCourant compte, TimeProvider horloge)
{
    internal const int Limite = 200;

    /// <summary>Dangers en cours du site (ni levés ni écartés), dossiers clos compris : les moins avancés d'abord, puis les plus anciens.</summary>
    internal async Task<IReadOnlyList<DangerEnCours>> EnCoursAsync(Guid organisationId, Guid siteId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var lignes = await (from a in context.AlertesDanger.AsNoTracking()
                            join s in context.Signalements.AsNoTracking() on a.SignalementId equals s.Id
                            where a.SiteId == siteId && a.Fin == null
                            select new { Alerte = a, Version = EF.Property<uint>(a, "xmin"), s.Description, s.Statut })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        // Tri sur l'état déduit, puis limite : un danger récent non pris en compte n'est jamais écarté par des plus anciens.
        var retenus = lignes.OrderBy(l => l.Alerte.Etat).ThenBy(l => l.Alerte.CreeLe).Take(Limite).ToList();
        var noms = await LectureDangers.NomsAsync(annuaire, retenus.Select(l => l.Alerte), cancellationToken).ConfigureAwait(false);
        return [.. retenus
            .Select(l => new DangerEnCours(
                l.Alerte.SignalementId, ServiceDossiers.Titre(l.Description), l.Statut.ToString(), LectureDangers.Lire(l.Alerte, l.Version, noms)))];
    }

    /// <summary>Prise en compte par un R/D : il suit le danger ; les nouvelles alertes s'arrêtent (Q4-A).</summary>
    internal Task PrendreEnCompteAsync(Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, DemandePriseEnCompte demande, CancellationToken cancellationToken) =>
        AgirAsync(organisationId, siteId, dossierId, dangerId, demande.Version, Droit.TraiterDanger, notifie: false, (dossier, alerte, auteur, le, _) =>
        {
            alerte.PrendreEnCompte(auteur, le);
            context.Tracer(dossier, NatureEvenementDossier.DangerPrisEnCompte, auteur, le, new { alerte = alerte.Id });
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>
    /// Mise en sécurité déclarée réalisée par T, R/D, P/C (Q5-A), dès l'alerte ouverte (Q-M1) ; notifiée au R/D qui a pris le
    /// danger en compte, aux R/D du site si personne (Q-M3).
    /// </summary>
    internal Task DeclarerMiseEnSecuriteAsync(
        Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, DemandeMiseEnSecurite demande, CancellationToken cancellationToken) =>
        AgirAsync(organisationId, siteId, dossierId, dangerId, demande.Version, Droit.DeclarerMiseEnSecurite, notifie: true, (dossier, alerte, auteur, le, site) =>
        {
            var action = Saisie.Valeur<ActionMiseEnSecurite>(demande.Action, "action");
            alerte.DeclarerMiseEnSecurite(action, demande.Commentaire, auteur, le);
            context.Tracer(dossier, NatureEvenementDossier.MiseEnSecuriteDeclaree, auteur, le,
                new { alerte = alerte.Id, action = action.ToString(), commentaire = alerte.SecuriteCommentaire });
            context.Emettre(TypeNotification.MiseEnSecuriteDeclaree, dossier, $"securite:{alerte.Id:N}:{le.ToUnixTimeMilliseconds()}",
                site.OuResponsables(alerte.PrisEnCompteParCompteId), auteur, le, alerte.Id);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>
    /// Vérification par un R/D : suffisante, le danger est maîtrisé ; sinon (motif) il redevient « Pris en compte », les rappels
    /// horaires repartent et le déclarant est notifié (Q-M2).
    /// </summary>
    internal Task VerifierMiseEnSecuriteAsync(
        Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, DemandeVerification demande, CancellationToken cancellationToken) =>
        AgirAsync(organisationId, siteId, dossierId, dangerId, demande.Version, Droit.TraiterDanger, notifie: demande.Suffisante == false, (dossier, alerte, auteur, le, site) =>
        {
            var suffisante = demande.Suffisante ?? throw new SaisieInvalideException("suffisante", "Indiquez si la mise en sécurité suffit.");
            var (declarant, action, motif) = alerte.VerifierMiseEnSecurite(suffisante, demande.Motif, auteur, le);
            if (suffisante)
            {
                context.Tracer(dossier, NatureEvenementDossier.MiseEnSecuriteVerifiee, auteur, le, new { alerte = alerte.Id, action = action.ToString() });
                return Task.CompletedTask;
            }

            context.Tracer(dossier, NatureEvenementDossier.MiseEnSecuriteInsuffisante, auteur, le,
                new { alerte = alerte.Id, action = action.ToString(), motif });
            context.Emettre(TypeNotification.MiseEnSecuriteInsuffisante, dossier, $"securite-insuffisante:{alerte.Id:N}:{le.ToUnixTimeMilliseconds()}",
                site.Membres([declarant]), auteur, le, alerte.Id);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Danger levé par un R/D (Q-M4) ; « réparation faite et dossier clôturé Résolu » est vérifié sur le dossier.</summary>
    internal Task LeverAsync(Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, DemandeFinDanger demande, CancellationToken cancellationToken) =>
        AgirAsync(organisationId, siteId, dossierId, dangerId, demande.Version, Droit.TraiterDanger, notifie: false, async (dossier, alerte, auteur, le, _) =>
        {
            var motif = Saisie.Valeur<MotifLevee>(demande.Motif, "motif");
            // Dossier clôturé « Résolu » : la clôture qui l'a clôturé, celle de la dernière intervention du cycle en cours.
            var resolu = motif == MotifLevee.ReparationDossierResolu && dossier.Statut == StatutDossier.Cloture
                && await context.Interventions.AsNoTracking()
                    .Where(i => i.DossierId == dossier.Id && i.ClotureeLe != null && (dossier.RouvertLe == null || i.CreeLe >= dossier.RouvertLe))
                    .OrderByDescending(i => i.ClotureeLe).ThenByDescending(i => i.Numero).Select(i => i.EtatFinal)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) == EtatFinal.Resolu;
            alerte.Lever(motif, demande.Commentaire, resolu, auteur, le);
            context.Tracer(dossier, NatureEvenementDossier.DangerLeve, auteur, le,
                new { alerte = alerte.Id, motif = alerte.FinMotif, commentaire = alerte.FinCommentaire });
        }, cancellationToken);

    /// <summary>Danger écarté par un R/D, sans mise en sécurité déclarée ; ce n'est pas un classement du dossier.</summary>
    internal Task EcarterAsync(Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, DemandeFinDanger demande, CancellationToken cancellationToken) =>
        AgirAsync(organisationId, siteId, dossierId, dangerId, demande.Version, Droit.TraiterDanger, notifie: false, (dossier, alerte, auteur, le, _) =>
        {
            var motif = Saisie.Valeur<MotifEcartement>(demande.Motif, "motif");
            alerte.Ecarter(motif, demande.Commentaire, auteur, le);
            context.Tracer(dossier, NatureEvenementDossier.DangerEcarte, auteur, le,
                new { alerte = alerte.Id, motif = alerte.FinMotif, commentaire = alerte.FinCommentaire });
            return Task.CompletedTask;
        }, cancellationToken);

    // Droit vérifié (organisation courante fixée), membres du site lus avant la transaction (autre module) s'il y a quelqu'un à
    // notifier, puis dossier
    // verrouillé : les actions sur ses dangers et sa mise en attente (Q6-A) sont sérialisées ; version du danger comparée.
    private async Task AgirAsync(
        Guid organisationId, Guid siteId, Guid dossierId, Guid dangerId, uint? version, Droit droit, bool notifie,
        Func<Signalement, AlerteDanger, Guid, DateTimeOffset, DestinatairesSite, Task> etape, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, droit, cancellationToken).ConfigureAwait(false);
        var attendue = version ?? throw new SaisieInvalideException("version", "Version du danger lue obligatoire.");
        var auteur = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        var site = await DestinatairesSite.LireSiAsync(notifie, annuaire, siteId, cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await context.VerrouillerAsync([dossierId], cancellationToken).ConfigureAwait(false);
        var dossier = await context.Signalements.SingleOrDefaultAsync(s => s.Id == dossierId && s.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new DangerIntrouvableException();
        var alerte = await context.AlertesDanger.SingleOrDefaultAsync(a => a.Id == dangerId && a.SignalementId == dossierId, cancellationToken).ConfigureAwait(false)
            ?? throw new DangerIntrouvableException();
        if (context.Entry(alerte).Property<uint>("xmin").CurrentValue != attendue)
        {
            throw new DossierModifieException();
        }

        await etape(dossier, alerte, auteur, horloge.GetUtcNow(), site).ConfigureAwait(false);
        await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lecture des dangers commune à la fiche du dossier, à la liste du suivi et au bandeau des dangers en cours.</summary>
internal static class LectureDangers
{
    internal static async Task<IReadOnlyDictionary<Guid, string>> NomsAsync(
        IAnnuaireMembres annuaire, IEnumerable<AlerteDanger> alertes, CancellationToken cancellationToken)
    {
        var comptes = Comptes(alertes);
        return comptes.Count == 0 ? new Dictionary<Guid, string>() : await annuaire.NomsAsync(comptes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Comptes nommés par le suivi des alertes (prise en compte, déclaration, vérification, fin).</summary>
    internal static List<Guid> Comptes(IEnumerable<AlerteDanger> alertes) =>
        [.. alertes.SelectMany(a => new[] { a.PrisEnCompteParCompteId, a.SecuriteDeclareeParCompteId, a.SecuriteVerifieeParCompteId, a.FinParCompteId })
            .Where(c => c is not null).Select(c => c!.Value).Distinct()];

    internal static DangerLu Lire(AlerteDanger a, uint version, IReadOnlyDictionary<Guid, string> noms)
    {
        string? Nom(Guid? c) => c is { } id ? noms.GetValueOrDefault(id) : null;
        var securite = a.SecuriteDeclareeLe is { } declaree
            ? new MiseEnSecuriteLue(
                a.SecuriteAction!.Value.ToString(), a.SecuriteCommentaire, declaree, a.SecuriteDeclareeParCompteId!.Value, Nom(a.SecuriteDeclareeParCompteId),
                a.SecuriteVerifieeLe, a.SecuriteVerifieeParCompteId, Nom(a.SecuriteVerifieeParCompteId))
            : null;
        var fin = a.Fin is { } nature
            ? new FinDangerLue(nature.ToString(), a.FinMotif!, a.FinCommentaire, a.FinLe!.Value, a.FinParCompteId, Nom(a.FinParCompteId))
            : null;
        return new DangerLu(
            a.Id, a.Etat.ToString(), a.CreeLe, a.ConstatId, a.PrisEnCompteLe, a.PrisEnCompteParCompteId, Nom(a.PrisEnCompteParCompteId), securite, fin, version);
    }

    /// <summary>État de danger de chaque dossier : celui de son alerte en cours la moins avancée (aucune entrée sans danger en cours).</summary>
    internal static async Task<IReadOnlyDictionary<Guid, string>> EtatsAsync(MaintenanceDbContext context, IReadOnlyCollection<Guid> dossiers, CancellationToken cancellationToken)
    {
        if (dossiers.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var alertes = await context.AlertesDanger.AsNoTracking().Where(a => dossiers.Contains(a.SignalementId) && a.Fin == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return alertes.GroupBy(a => a.SignalementId).ToDictionary(g => g.Key, g => g.Min(a => a.Etat).ToString());
    }
}
