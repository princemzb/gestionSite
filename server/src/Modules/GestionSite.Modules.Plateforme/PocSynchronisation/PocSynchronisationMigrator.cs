using GestionSite.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GestionSite.Modules.Plateforme.PocSynchronisation;

/// <summary>
/// Tables du POC créées par script idempotent (pas de migrations EF : code jetable). Mêmes règles que le
/// produit : RLS activée et forcée par organisation, droits minimaux du rôle applicatif.
/// </summary>
internal sealed class PocSynchronisationMigrator(
    IOptions<PocSynchronisationOptions> options, NpgsqlDataSource application, IServiceProvider services) : IDatabaseMigrator
{
    private const string Organisation = "NULLIF(current_setting('app.organisation_id', true), '')::uuid";

    private static readonly string Script = $"""
        CREATE SCHEMA IF NOT EXISTS poc_sync;
        CREATE TABLE IF NOT EXISTS poc_sync.notes (
            id uuid PRIMARY KEY,
            organisation_id uuid NOT NULL,
            site_id uuid NOT NULL,
            texte varchar(2000) NOT NULL,
            version integer NOT NULL,
            auteur_compte_id uuid NOT NULL,
            modifie_le timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS poc_sync.commandes (
            operation_id uuid PRIMARY KEY,
            organisation_id uuid NOT NULL,
            device_id varchar(100) NOT NULL,
            auteur_compte_id uuid NOT NULL,
            empreinte bytea NOT NULL,
            resultat varchar(20) NOT NULL,
            note_id uuid NOT NULL,
            version integer NOT NULL,
            recue_le timestamptz NOT NULL);
        ALTER TABLE poc_sync.notes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE poc_sync.notes FORCE ROW LEVEL SECURITY;
        ALTER TABLE poc_sync.commandes ENABLE ROW LEVEL SECURITY;
        ALTER TABLE poc_sync.commandes FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS isolation_organisation ON poc_sync.notes;
        CREATE POLICY isolation_organisation ON poc_sync.notes
            USING (organisation_id = {Organisation}) WITH CHECK (organisation_id = {Organisation});
        DROP POLICY IF EXISTS isolation_organisation ON poc_sync.commandes;
        CREATE POLICY isolation_organisation ON poc_sync.commandes
            USING (organisation_id = {Organisation}) WITH CHECK (organisation_id = {Organisation});
        GRANT USAGE ON SCHEMA poc_sync TO gestionsite_app;
        GRANT SELECT, INSERT, UPDATE ON poc_sync.notes TO gestionsite_app;
        GRANT SELECT, INSERT ON poc_sync.commandes TO gestionsite_app;
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

        await using var command = application.CreateCommand("SELECT to_regclass('poc_sync.commandes') IS NULL");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}
