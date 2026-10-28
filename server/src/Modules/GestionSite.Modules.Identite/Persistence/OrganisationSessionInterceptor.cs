using System.Data.Common;
using GestionSite.SharedKernel;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionSite.Modules.Identite.Persistence;

/// <summary>
/// Seconde barrière (RLS) : à chaque ouverture de connexion, la variable de session lue par les
/// politiques reçoit l'organisation courante, ou une valeur vide (aucune ligne visible). Écrite à
/// chaque ouverture : une connexion réutilisée du pool ne garde jamais l'organisation précédente.
/// </summary>
internal sealed class OrganisationSessionInterceptor(OrganisationCourante organisationCourante) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('" + OrganisationCourante.VariableSession + "', @organisation, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreerCommande(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreerCommande(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreerCommande(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        var parametre = command.CreateParameter();
        parametre.ParameterName = "organisation";
        parametre.Value = organisationCourante.Id?.ToString() ?? string.Empty;
        command.Parameters.Add(parametre);
        return command;
    }
}
