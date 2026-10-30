using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Patrimoine.Composants;
using GestionSite.Modules.Patrimoine.Domain;
using GestionSite.Modules.Patrimoine.Grilles;
using GestionSite.Modules.Patrimoine.Persistence;
using GestionSite.Modules.Patrimoine.Structure;
using GestionSite.SharedKernel;
using GestionSite.SharedKernel.Photos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Patrimoine.Evaluations;

/// <summary>Résultat d'une évaluation : note affichée (une décimale), son libellé, et la proposition d'ouvrir un dossier.</summary>
internal sealed record EvaluationCreee(Guid Id, decimal Note, string Libelle, bool ProposerDossier);

internal sealed record EvaluationLue(
    Guid Id, DateTimeOffset EvalueLe, Guid EvaluateurCompteId, string? Evaluateur, int GrilleVersion, string? Etat, RepartitionLue? Repartition,
    decimal Note, string Libelle, IReadOnlyList<PointEvalue> Points, IReadOnlyList<MesureEvaluee> Mesures, string? Remarque, int Photos);

/// <summary>
/// Évaluations des composants (US-11, S11-02) : par le technicien, le responsable ou le délégué du site, avec la grille en
/// vigueur. La répartition d'un lot dans l'inventaire devient celle de sa dernière évaluation (journalisée). Consultées
/// par les comptes internes du site, jamais modifiées.
/// </summary>
internal sealed class ServiceEvaluations(
    PatrimoineDbContext context, IAutorisations autorisations, ServiceGrilles grilles, IAnnuaireMembres annuaire, IStockageFichiers stockage,
    CompteCourant compte, TimeProvider horloge)
{
    /// <summary>
    /// Évaluation d'un composant. Le formulaire (<paramref name="lire"/>) n'est lu qu'après le contrôle d'accès et du
    /// composant : 403/404 avant toute erreur de saisie, aucune photo reçue pour une demande refusée.
    /// </summary>
    internal async Task<EvaluationCreee> EvaluerAsync(
        Guid organisationId, Guid siteId, Guid composantId, Func<Task<(DemandeEvaluation Demande, IReadOnlyList<PhotoRecue> Photos)>> lire,
        CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.EvaluerComposant, cancellationToken).ConfigureAwait(false);
        var composant = await context.Composants.SingleOrDefaultAsync(c => c.Id == composantId && c.SiteId == siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new ElementIntrouvableException();
        var (demande, photos) = await lire().ConfigureAwait(false);
        var commandeId = demande.CommandeId is { } commande && commande != Guid.Empty
            ? commande
            : throw new SaisieInvalideException("commandeId", "Identifiant d'envoi obligatoire.");
        var evaluateur = compte.Id ?? throw new InvalidOperationException("Compte courant non défini.");
        var saisie = new SaisieEvaluation(
            commandeId, demande.GrilleVersion, demande.Constats, demande.Mesures,
            string.IsNullOrWhiteSpace(demande.Etat) ? null : SaisieComposant.Valeur<EtatComposant>(demande.Etat, "etat"),
            SaisieComposant.Repartition(demande.Repartition), demande.Remarque, photos.Count);
        return Resultat(await EnregistrerAsync(siteId, composant, saisie, photos, evaluateur, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Évaluation contrôlée puis enregistrée pour un composant du site (accès déjà vérifié par l'appelant : API d'US-11 ou
    /// inspection d'US-12) ; un rejeu de la même commande rend l'évaluation déjà enregistrée.
    /// </summary>
    internal async Task<Evaluation> EnregistrerAsync(
        Guid siteId, Composant composant, SaisieEvaluation saisie, IReadOnlyList<PhotoRecue> photos, Guid evaluateur, CancellationToken cancellationToken)
    {
        if (await DejaEvalueeAsync(evaluateur, composant.Id, saisie, cancellationToken).ConfigureAwait(false) is { } deja)
        {
            return deja; // rejeu reconnu avant les contrôles : une grille modifiée depuis l'envoi ne le refuse pas
        }

        // 1. Contrôles complets (grille en vigueur, saisie, photo exigée) avant tout décodage d'image.
        var courante = await grilles.CouranteAsync(siteId, composant.CorpsEtat, cancellationToken).ConfigureAwait(false);
        var grille = new GrilleEvaluee(courante.Version, courante.Contenu.Criteres, courante.Contenu.Mesures);
        var maintenant = horloge.GetUtcNow();
        var evaluation = Evaluation.Creer(Guid.CreateVersion7(maintenant), composant, grille, saisie, evaluateur, maintenant);

        // 2. Photos préparées puis écrites avant l'enregistrement, sous des clés propres à cette évaluation ; retirées si
        //    l'enregistrement n'aboutit pas (conflit, envoi concurrent perdant).
        var jpegs = photos.Select(p => TraitementPhotos.Preparer(p.Contenu)).ToList();
        await using var envoi = await PhotosStockees.EcrireAsync(
            stockage, jpegs, (rang, jpeg) => new PhotoEvaluation(Guid.CreateVersion7(maintenant), evaluation, rang, jpeg.Length, maintenant), p => p.Cle,
            photos => { var id = photos[0].Id; return context.PhotosEvaluation.AsNoTracking().AnyAsync(p => p.Id == id, CancellationToken.None); }, cancellationToken).ConfigureAwait(false);
        context.PhotosEvaluation.AddRange(envoi.Photos);
        context.Evaluations.Add(evaluation);

        // 3. Lot : répartition de l'inventaire reprise de l'évaluation, toujours à la version lue du composant (xmin), même
        //    inchangée : une évaluation ou une modification concurrente du lot est refusée (409) au lieu d'être écrasée.
        if (evaluation.Repartition is { } repartition)
        {
            var avant = SaisieComposant.Releve(composant.Lot?.Repartition);
            composant.Repartir(repartition);
            context.Entry(composant).Property(c => c.QuantiteBon).IsModified = true;
            var apres = SaisieComposant.Releve(repartition);
            var changes = apres.Keys.Where(etat => avant[etat] != apres[etat]).ToList();
            if (changes.Count > 0)
            {
                context.Tracer(evaluateur, maintenant, siteId, CibleInventaire.Composant, composant.Id, NatureEvenementInventaire.Evaluation,
                    changes.ToDictionary(etat => etat, etat => avant[etat]), changes.ToDictionary(etat => etat, etat => apres[etat]));
            }
        }

        try
        {
            await context.EnregistrerAsync(cancellationToken).ConfigureAwait(false);
            envoi.Confirmer();
        }
        catch (Exception concurrent) when (concurrent is ModifieEntreTempsException
            || (concurrent is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation }
                && violation.ConstraintName == Evaluation.ContrainteCommande))
        {
            // Même envoi reçu en parallèle : l'autre requête l'a enregistré (doublon de commande, ou lot déjà mis à jour par
            // elle) ; même réponse que pour un rejeu. Sinon, conflit réel.
            context.ChangeTracker.Clear();
            if (await DejaEvalueeAsync(evaluateur, composant.Id, saisie, cancellationToken).ConfigureAwait(false) is not { } enregistree)
            {
                throw;
            }

            return enregistree;
        }

        return evaluation;
    }

    /// <summary>Évaluations du composant, la plus récente d'abord (retiré compris).</summary>
    internal async Task<IReadOnlyList<EvaluationLue>> ListerAsync(Guid organisationId, Guid siteId, Guid composantId, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        if (!await context.Composants.AnyAsync(c => c.Id == composantId && c.SiteId == siteId, cancellationToken).ConfigureAwait(false))
        {
            throw new ElementIntrouvableException();
        }

        var evaluations = await context.Evaluations.AsNoTracking().Where(e => e.SiteId == siteId && e.ComposantId == composantId)
            .OrderByDescending(e => e.EvalueLe).ThenByDescending(e => e.Id)
            .Select(e => new { Evaluation = e, Photos = context.PhotosEvaluation.Count(p => p.EvaluationId == e.Id) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var noms = await annuaire.NomsAsync([.. evaluations.Select(e => e.Evaluation.EvaluateurCompteId).Distinct()], cancellationToken).ConfigureAwait(false);
        return [.. evaluations.Select(lue =>
        {
            var e = lue.Evaluation;
            return new EvaluationLue(
                e.Id, e.EvalueLe, e.EvaluateurCompteId, noms.GetValueOrDefault(e.EvaluateurCompteId), e.GrilleVersion, e.Etat?.ToString(),
                e.Repartition is { } r ? new RepartitionLue(r.Bon, r.Satisfaisant, r.Degrade, r.Mauvais, r.Critique) : null,
                Echelle.Arrondie(e.Note), Echelle.Libelle(e.Note).ToString(), e.PointsLus, e.MesuresLues, e.Remarque, lue.Photos);
        })];
    }

    /// <summary>Photo d'une évaluation (JPEG), ou null si elle n'existe pas pour cette évaluation de ce composant du site.</summary>
    internal async Task<byte[]?> PhotoAsync(
        Guid organisationId, Guid siteId, Guid composantId, Guid evaluationId, int rang, CancellationToken cancellationToken)
    {
        await autorisations.ExigerAsync(organisationId, siteId, Droit.ConsulterSite, cancellationToken).ConfigureAwait(false);
        var cle = await (
                from p in context.PhotosEvaluation.AsNoTracking()
                join e in context.Evaluations.AsNoTracking() on p.EvaluationId equals e.Id
                where e.SiteId == siteId && e.ComposantId == composantId && e.Id == evaluationId && p.Rang == rang
                select p.Cle)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return cle is null ? null : (await stockage.LireAsync(cle, cancellationToken).ConfigureAwait(false))?.Contenu;
    }

    // Rejeu : même évaluateur, même composant, même état → même évaluation ; sinon l'identifiant d'envoi est réutilisé à tort (409).
    private async Task<Evaluation?> DejaEvalueeAsync(Guid evaluateur, Guid composantId, SaisieEvaluation saisie, CancellationToken cancellationToken)
    {
        var recue = await context.Evaluations.AsNoTracking().Where(e => e.CommandeId == saisie.CommandeId)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return recue switch
        {
            null => null,
            _ when recue.MemeEnvoi(evaluateur, composantId, saisie) => recue,
            _ => throw new CommandeReutiliseeException(),
        };
    }

    private static EvaluationCreee Resultat(Evaluation evaluation) =>
        new(evaluation.Id, Echelle.Arrondie(evaluation.Note), Echelle.Libelle(evaluation.Note).ToString(), evaluation.ProposerDossier);
}
