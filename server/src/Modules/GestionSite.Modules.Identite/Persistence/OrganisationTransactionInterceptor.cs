using System.Data.Common;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>Pose l'organisation courante (SET LOCAL) dès l'ouverture d'une transaction EF ; voir OrganisationCommandInterceptor.</summary>
internal sealed class OrganisationTransactionInterceptor(OrganisationCourante organisationCourante, CompteCourant compteCourant) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        organisationCourante.SignalerTransaction(ouverte: true);
        using var command = Commande(connection, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        organisationCourante.SignalerTransaction(ouverte: true);
        await using var command = Commande(connection, result);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Transaction ouverte ailleurs puis confiée à EF (UseTransaction) : même traitement.
    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result) =>
        TransactionStarted(connection, null!, result);

    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
        TransactionStartedAsync(connection, null!, result, cancellationToken);

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        organisationCourante.SignalerTransaction(ouverte: false);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        organisationCourante.SignalerTransaction(ouverte: false);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        organisationCourante.SignalerTransaction(ouverte: false);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        organisationCourante.SignalerTransaction(ouverte: false);
        return Task.CompletedTask;
    }

    private DbCommand Commande(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = OrganisationCommandInterceptor.Instruction(organisationCourante, compteCourant);
        return command;
    }
}
