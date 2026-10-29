using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Persistence;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestionSite.Modules.Maintenance.DepotPublic;

/// <summary>Envoi du formulaire public ; la commande (identifiant choisi par le formulaire) rend le rejeu sans effet.</summary>
/// <remarks><c>Verification</c> : jeton anti-robot du navigateur (Turnstile), à usage unique.</remarks>
internal sealed record DemandeDepot(
    Guid CommandeId, string? Nature, string? Description, string? Emplacement, bool Danger, bool ActiviteBloquee, string? Nom, string? Contact,
    string? Verification = null);

/// <summary>
/// Accusé sans aucun moyen de lire le signalement (F-01) ; en cas de danger, le numéro du site à appeler s'il est
/// renseigné (décision du 28/10).
/// </summary>
internal sealed record AccuseDepot(string Site, bool Danger, string? NumeroUrgence);

/// <summary>Commande déjà utilisée pour un autre contenu (CONTRATS : même clé, contenu différent → erreur).</summary>
internal sealed class CommandeReutiliseeException() : Exception("Cet identifiant d'envoi a déjà servi pour un autre signalement.");

/// <summary>Dépôt public sans compte (US-08, RG-02) : site atteint par son code, signalement créé une seule fois.</summary>
internal sealed class ServiceDepotPublic(
    MaintenanceDbContext context, ISitesPublics sites, IStockageFichiers stockage, IVerificationHumaine verification, TimeProvider horloge)
{
    internal Task<SitePublic?> SiteAsync(string code, CancellationToken cancellationToken) => sites.OuvrirAsync(code, cancellationToken);

    /// <summary>L'accusé, ou null si le code de site est inconnu (404).</summary>
    /// <exception cref="SaisieInvalideException">Champ manquant ou invalide (422).</exception>
    /// <exception cref="CommandeReutiliseeException">Même commande, contenu différent (409).</exception>
    internal async Task<AccuseDepot?> DeposerAsync(
        string code, DemandeDepot demande, IReadOnlyList<PhotoRecue> photos, CancellationToken cancellationToken)
    {
        if (photos.Count > TraitementPhotos.NombreMax)
        {
            throw new SaisieInvalideException("photos", "Trois photos au plus.");
        }

        // Avant toute transaction : la recherche fixe l'organisation courante (filtres et RLS).
        if (await sites.OuvrirAsync(code, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return null;
        }

        var maintenant = horloge.GetUtcNow();
        var signalement = new Signalement(
            Guid.CreateVersion7(maintenant), site.OrganisationId, site.SiteId, demande.CommandeId, Nature(demande.Nature), demande.Description,
            demande.Emplacement, demande.Danger, demande.ActiviteBloquee, demande.Nom, demande.Contact, maintenant);
        if (await DejaRecuAsync(signalement, cancellationToken).ConfigureAwait(false) is { } accuse)
        {
            return accuse;
        }

        // Après le contrôle de rejeu : le jeton anti-robot ne sert qu'une fois, un renvoi après coupure réseau
        // reçoit son accusé sans nouvelle vérification. Avant tout traitement de photo.
        if (!await verification.VerifierAsync(demande.Verification, cancellationToken).ConfigureAwait(false))
        {
            throw new SaisieInvalideException("verification", "Vérification anti-robot échouée : réessayez.");
        }

        // Toutes traitées d'abord : une photo refusée ne laisse aucun fichier écrit. Écrites ensuite avant
        // l'enregistrement, sous des clés propres à ce signalement : un échec de la base (ou un envoi concurrent
        // perdant) laisse au pire des fichiers orphelins, jamais les photos d'un autre signalement remplacées.
        var jpegs = photos.Select(p => TraitementPhotos.Preparer(p.Contenu)).ToList();
        var enregistrees = jpegs.Select((jpeg, i) => new PhotoSignalement(Guid.CreateVersion7(maintenant), signalement, i + 1, jpeg.Length)).ToList();
        await Task.WhenAll(enregistrees.Select((photo, i) =>
            stockage.EcrireAsync(photo.Cle, jpegs[i], TraitementPhotos.TypeContenu, cancellationToken))).ConfigureAwait(false);
        context.Photos.AddRange(enregistrees);

        context.Signalements.Add(signalement);
        if (signalement.Danger)
        {
            context.AlertesDanger.Add(new AlerteDanger(Guid.CreateVersion7(maintenant), signalement));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException doublon) when (doublon.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Même envoi reçu en parallèle : l'autre requête l'a enregistré ; même réponse que pour un rejeu.
            context.ChangeTracker.Clear();
            return await DejaRecuAsync(signalement, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException(
                "Doublon signalé par la base mais introuvable.", doublon);
        }

        return Accuse(site.NomSite, signalement.Danger, site.NumeroUrgence);

        async Task<AccuseDepot?> DejaRecuAsync(Signalement envoye, CancellationToken ct)
        {
            var recu = await context.Signalements.AsNoTracking().SingleOrDefaultAsync(s => s.CommandeId == envoye.CommandeId, ct)
                .ConfigureAwait(false);
            return recu switch
            {
                null => null,
                _ when recu.MemeEnvoi(envoye) => Accuse(site.NomSite, recu.Danger, site.NumeroUrgence),
                _ => throw new CommandeReutiliseeException(),
            };
        }
    }

    private static AccuseDepot Accuse(string site, bool danger, string? numeroUrgence) => new(site, danger, danger ? numeroUrgence : null);

    private static NatureSignalement Nature(string? nature) =>
        Enum.TryParse<NatureSignalement>(nature, ignoreCase: true, out var valeur) && Enum.IsDefined(valeur) && !int.TryParse(nature, out _)
            ? valeur
            : throw new SaisieInvalideException(nameof(nature), "Nature attendue : Incident ou Demande.");
}
