using System.Data.Common;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Seconde barrière (RLS) : la variable lue par les politiques est posée par SET LOCAL, donc pour une
/// seule transaction : elle ne peut fuir vers une autre requête par le pool et reste juste derrière un
/// pooler en mode transaction.
/// - Commande hors transaction (lecture) : SET LOCAL en tête du même lot, que PostgreSQL exécute comme
///   une transaction implicite ; aucun aller-retour de plus.
/// - Transaction (toute écriture : AutoTransactionBehavior.Always) : SET LOCAL à son ouverture, car EF
///   lit les résultats d'un lot d'écriture par position et un préfixe les décalerait.
/// </summary>
internal sealed class OrganisationCommandInterceptor(OrganisationCourante organisationCourante, CompteCourant compteCourant) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Prefixer(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Prefixer(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Prefixer(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Prefixer(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Prefixer(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Prefixer(command);
        return ValueTask.FromResult(result);
    }

    private void Prefixer(DbCommand command)
    {
        if (command.Transaction is null)
        {
            command.CommandText = $"{Instruction(organisationCourante, compteCourant)}\n{command.CommandText}";
        }
    }

    // Valeur littérale sûre : un Guid formaté par .NET (chiffres hexadécimaux et tirets) ou vide.
    // SET n'accepte pas de paramètre ; il ne renvoie aucun résultat, le lecteur passe à la suite.
    internal static string Instruction(OrganisationCourante organisationCourante, CompteCourant compteCourant)
    {
        organisationCourante.MarquerTransmise();
        return $"SET LOCAL {OrganisationCourante.VariableSession} = '{Litteral(organisationCourante.Id)}'; " +
            $"SET LOCAL {CompteCourant.VariableSession} = '{Litteral(compteCourant.Id)}';";
    }

    private static string Litteral(Guid? id) => id?.ToString("D") ?? string.Empty;
}
