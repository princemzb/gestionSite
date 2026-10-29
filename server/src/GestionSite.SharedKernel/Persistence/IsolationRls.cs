using Microsoft.EntityFrameworkCore.Migrations;

namespace GestionSite.SharedKernel.Persistence;

/// <summary>
/// Seconde barrière (ADR-10) des migrations : sans organisation de session, aucune ligne visible ni écrite.
/// Texte figé (test IsolationRlsTests) : des migrations déjà appliquées l'utilisent ; une évolution passe par
/// une nouvelle méthode et une nouvelle migration, jamais par la modification de celle-ci.
/// </summary>
public static class IsolationRls
{
    private const string OrganisationDeSession = "NULLIF(current_setting('app.organisation_id', true), '')::uuid";

    public static void IsolerParOrganisation(this MigrationBuilder migrationBuilder, string schema, string table, string colonne = "organisation_id")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {schema}.{table} FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            $"CREATE POLICY isolation_organisation ON {schema}.{table} " +
            $"USING ({colonne} = {OrganisationDeSession}) WITH CHECK ({colonne} = {OrganisationDeSession});");
    }
}
