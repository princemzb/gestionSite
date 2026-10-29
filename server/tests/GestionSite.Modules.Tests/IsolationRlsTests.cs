using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GestionSite.Modules.Tests;

/// <summary>
/// Texte figé de la politique d'isolation : des migrations déjà appliquées (Maintenance, Patrimoine) l'utilisent ; le
/// changer ferait diverger une base neuve d'une base existante pour la même migration.
/// </summary>
public sealed class IsolationRlsTests
{
    [Fact]
    public void TheIsolationPolicyTextNeverChanges()
    {
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.IsolerParOrganisation("patrimoine", "composants");

        Assert.Equal(
        [
            "ALTER TABLE patrimoine.composants ENABLE ROW LEVEL SECURITY;",
            "ALTER TABLE patrimoine.composants FORCE ROW LEVEL SECURITY;",
            "CREATE POLICY isolation_organisation ON patrimoine.composants " +
            "USING (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid) " +
            "WITH CHECK (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid);",
        ], migration.Operations.Cast<SqlOperation>().Select(o => o.Sql));
    }
}
