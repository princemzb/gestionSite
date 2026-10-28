using System.Security.Cryptography;
using System.Text;
using GestionSite.SharedKernel;
using Npgsql;

namespace GestionSite.Modules.Plateforme.PocSynchronisation;

/// <summary>Commande hors connexion (docs/dev/SYNCHRONISATION.md) : modifier le texte d'une note de terrain.</summary>
internal sealed record CommandeNote(
    Guid OperationId, string DeviceId, Guid ActeurId, Guid SiteId, Guid NoteId, int BaseVersion, string Texte, DateTimeOffset ObserveLe);

/// <summary>applied | alreadyApplied | conflict ; en conflit, l'état serveur est renvoyé pour l'arbitrage.</summary>
internal sealed record ResultatCommande(Guid OperationId, string Resultat, int Version, string? TexteCourant);

internal sealed class OperationReutiliseeException() : Exception("Identifiant d'opération déjà utilisé pour un autre contenu.");

/// <summary>Note d'un autre site que celui de la commande (sur lequel l'autorisation a été vérifiée).</summary>
internal sealed class NoteHorsSiteException() : Exception("Note introuvable sur ce site.");

/// <summary>
/// Application idempotente : une même opération (même identifiant, même contenu, même auteur) donne
/// toujours le même résultat, qu'elle soit reçue une ou dix fois, y compris après une coupure survenue
/// entre la validation serveur et la réponse. La version attendue détecte les modifications concurrentes,
/// sans écrasement silencieux. Une transaction par commande, sous l'organisation de la route (RLS).
/// </summary>
internal sealed class ServiceCommandes(NpgsqlDataSource application, TimeProvider horloge)
{
    internal const string Applique = "applied";
    internal const string DejaApplique = "alreadyApplied";
    internal const string Conflit = "conflict";

    internal async Task<ResultatCommande> AppliquerAsync(Guid organisationId, Guid auteur, CommandeNote commande, CancellationToken cancellationToken)
    {
        var empreinte = Empreinte(commande);
        for (var essai = 0; ; essai++)
        {
            try
            {
                return await EssayerAsync(organisationId, auteur, commande, empreinte, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException erreur) when (erreur.SqlState == PostgresErrorCodes.UniqueViolation && essai == 0)
            {
                // Doublon reçu au même instant : l'autre transaction a gagné, la relecture rendra son résultat.
            }
        }
    }

    private async Task<ResultatCommande> EssayerAsync(
        Guid organisationId, Guid auteur, CommandeNote commande, byte[] empreinte, CancellationToken cancellationToken)
    {
        await using var connexion = await application.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connexion.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var organisation = Commande(
            connexion, $"SELECT set_config('{OrganisationCourante.VariableSession}', @org, true)", ("org", organisationId.ToString("D"))))
        {
            await organisation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deja = Commande(connexion,
            "SELECT empreinte, auteur_compte_id, resultat, version, texte_serveur FROM poc_synchronisation.commandes WHERE operation_id = @op",
            ("op", commande.OperationId)))
        await using (var lecture = await deja.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await lecture.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var memeContenu = ((byte[])lecture[0]).AsSpan().SequenceEqual(empreinte) && lecture.GetGuid(1) == auteur;
                if (!memeContenu)
                {
                    throw new OperationReutiliseeException();
                }

                var resultat = lecture.GetString(2);
                // Même décision qu'au premier passage, état serveur du conflit compris.
                return new ResultatCommande(
                    commande.OperationId, resultat == Applique ? DejaApplique : resultat, lecture.GetInt32(3),
                    lecture.IsDBNull(4) ? null : lecture.GetString(4));
            }
        }

        // Verrou de la note : deux appareils qui la modifient en même temps sont départagés par sa version.
        int? versionCourante = null;
        string? texteCourant = null;
        await using (var note = Commande(
            connexion, "SELECT version, texte, site_id FROM poc_synchronisation.notes WHERE id = @id FOR UPDATE", ("id", commande.NoteId)))
        await using (var lecture = await note.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await lecture.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Le droit a été vérifié sur le site de la commande : la note doit en être.
                if (lecture.GetGuid(2) != commande.SiteId)
                {
                    throw new NoteHorsSiteException();
                }

                versionCourante = lecture.GetInt32(0);
                texteCourant = lecture.GetString(1);
            }
        }

        var maintenant = horloge.GetUtcNow();
        ResultatCommande sortie;
        if ((versionCourante ?? 0) != commande.BaseVersion)
        {
            sortie = new ResultatCommande(commande.OperationId, Conflit, versionCourante ?? 0, texteCourant);
        }
        else
        {
            var nouvelle = commande.BaseVersion + 1;
            var sql = versionCourante is null
                ? "INSERT INTO poc_synchronisation.notes (id, organisation_id, site_id, texte, version, auteur_compte_id, modifie_le) VALUES (@id, @org, @site, @texte, @version, @auteur, @le)"
                : "UPDATE poc_synchronisation.notes SET texte = @texte, version = @version, auteur_compte_id = @auteur, modifie_le = @le WHERE id = @id";
            await using var ecriture = Commande(connexion, sql, ("id", commande.NoteId), ("org", organisationId), ("site", commande.SiteId),
                ("texte", commande.Texte), ("version", nouvelle), ("auteur", auteur), ("le", maintenant));
            await ecriture.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            sortie = new ResultatCommande(commande.OperationId, Applique, nouvelle, null);
        }

        await using (var trace = Commande(connexion,
            "INSERT INTO poc_synchronisation.commandes (operation_id, organisation_id, device_id, auteur_compte_id, empreinte, resultat, note_id, version, texte_serveur, recue_le) " +
            "VALUES (@op, @org, @device, @auteur, @empreinte, @resultat, @note, @version, @texte, @le)",
            ("op", commande.OperationId), ("org", organisationId), ("device", commande.DeviceId), ("auteur", auteur), ("empreinte", empreinte),
            ("resultat", sortie.Resultat), ("note", commande.NoteId), ("version", sortie.Version),
            ("texte", (object?)sortie.TexteCourant ?? DBNull.Value), ("le", maintenant)))
        {
            await trace.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return sortie;
    }

    // Contenu de l'opération ; l'instant observé et l'appareil n'en font pas partie (renvoi depuis un autre état réseau).
    private static byte[] Empreinte(CommandeNote commande) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{commande.SiteId:D}|{commande.NoteId:D}|{commande.BaseVersion}|{commande.Texte}"));

    private static NpgsqlCommand Commande(NpgsqlConnection connexion, string sql, params (string Nom, object Valeur)[] parametres)
    {
        var commande = new NpgsqlCommand(sql, connexion);
        foreach (var (nom, valeur) in parametres)
        {
            commande.Parameters.AddWithValue(nom, valeur);
        }

        return commande;
    }
}
