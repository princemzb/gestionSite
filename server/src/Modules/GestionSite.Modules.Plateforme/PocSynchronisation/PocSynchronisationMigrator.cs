using GestionSite.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestionSite.Modules.Plateforme.PocSynchronisation;

/// <summary>
/// Tables du POC créées par script idempotent (pas de migrations EF : code jetable). Mêmes règles que le
/// produit : RLS activée et forcée par organisation, droits minimaux du rôle applicatif. Clés préfixées par
/// l'organisation : un identifiant choisi par un appareil ne peut pas entrer en collision avec une autre.
/// </summary>
internal sealed class PocSynchronisationMigrator(
    IOptions<PocSynchronisationOptions> options, NpgsqlDataSource application, IServiceProvider services) : IDatabaseMigrator
{
    private const string Organisation = "NULLIF(current_setting('" + OrganisationCourante.VariableSession + "', true), '')::uuid";

    private static readonly string Script = $"""
        CREATE SCHEMA IF NOT EXISTS poc_synchronisation;
        CREATE TABLE IF NOT EXISTS poc_synchronisation.notes (
            id uuid NOT NULL,
            organisation_id uuid NOT NULL,
            site_id uuid NOT NULL,
            texte varchar(2000) NOT NULL,
            version integer NOT NULL,
            auteur_compte_id uuid NOT NULL,
            modifie_le timestamptz NOT NULL,
            PRIMARY KEY (organisation_id, id));
        CREATE TABLE IF NOT EXISTS poc_synchronisation.commandes (
            operation_id uuid NOT NULL,
            organisation_id uuid NOT NULL,
            device_id varchar(100) NOT NULL,
            auteur_compte_id uuid NOT NULL,
            empreinte bytea NOT NULL,
            resultat varchar(20) NOT NULL,
            note_id uuid NOT NULL,
            version integer NOT NULL,
            texte_serveur varchar(2000),
            recue_le timestamptz NOT NULL,
            PRIMARY KEY (organisation_id, operation_id));
        ALTER TABLE poc_synchronisation.notes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE poc_synchronisation.notes FORCE ROW LEVEL SECURITY;
        ALTER TABLE poc_synchronisation.commandes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE poc_synchronisation.commandes FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS isolation_organisation ON poc_synchronisation.notes;
        CREATE POLICY isolation_organisation ON poc_synchronisation.notes
            USING (organisation_id = {Organisation}) WITH CHECK (organisation_id = {Organisation});
        DROP POLICY IF EXISTS isolation_organisation ON poc_synchronisation.commandes;
        CREATE POLICY isolation_organisation ON poc_synchronisation.commandes
            USING (organisation_id = {Organisation}) WITH CHECK (organisation_id = {Organisation});
        GRANT USAGE ON SCHEMA poc_synchronisation TO gestionsite_app;
        GRANT SELECT, INSERT, UPDATE ON poc_synchronisation.notes TO gestionsite_app;
        GRANT SELECT, INSERT ON poc_synchronisation.commandes TO gestionsite_app;
        """;

    public string ModuleName => "Plateforme (POC synchronisation)";

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Active)
        {
            return;
        }

        var proprietaire = services.GetRequiredKeyedService<NpgsqlDataSource>(DatabaseConnections.Migrations);
        await using var command = proprietaire.CreateCommand(Script);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Active)
        {
            return false;
        }

        await using var command = application.CreateCommand("SELECT to_regclass('poc_synchronisation.commandes') IS NULL");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}
