using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SitesMembresRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membres",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoque_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membres", x => x.id);
                    table.UniqueConstraint("ak_membres_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.ForeignKey(
                        name: "fk_membres_organisations_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "identite",
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                    table.UniqueConstraint("ak_sites_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.ForeignKey(
                        name: "fk_sites_organisations_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "identite",
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles_attribues",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_attribues", x => x.id);
                    table.CheckConstraint("ck_roles_attribues_site_selon_role", "(role = 'Administrateur') = (site_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_roles_attribues_membres_membre_id_organisation_id",
                        columns: x => new { x.membre_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "membres",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_roles_attribues_sites_site_id_organisation_id",
                        columns: x => new { x.site_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membres_organisation_id_compte_id",
                schema: "identite",
                table: "membres",
                columns: new[] { "organisation_id", "compte_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_attribues_membre_id_organisation_id_role_site_id",
                schema: "identite",
                table: "roles_attribues",
                columns: new[] { "membre_id", "organisation_id", "role", "site_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_roles_attribues_site_id_organisation_id",
                schema: "identite",
                table: "roles_attribues",
                columns: new[] { "site_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sites_organisation_id",
                schema: "identite",
                table: "sites",
                column: "organisation_id");

            ActiverIsolation(migrationBuilder);
            AccorderRoleApplicatif(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retour à l'état d'avant : plus aucun droit du rôle applicatif (sinon accès sans RLS).
            migrationBuilder.Sql("REVOKE ALL ON identite.organisations FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE SELECT ON identite.__ef_migrations FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE USAGE ON SCHEMA identite FROM gestionsite_app;");
            // Politiques des tables supprimées ci-dessous : supprimées avec elles.
            migrationBuilder.Sql("DROP POLICY isolation_organisation ON identite.organisations;");
            migrationBuilder.Sql("ALTER TABLE identite.organisations NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE identite.organisations DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "roles_attribues",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "membres",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "identite");
        }

        // Seconde barrière d'isolation (ADR-10) : RLS activée ET forcée, pour que le propriétaire
        // des tables (rôle de migration) y soit soumis lui aussi. Sans organisation de session
        // (variable vide ou absente), la valeur comparée est NULL : aucune ligne visible ni écrite.
        // Nom figé (OrganisationCourante.VariableSession) : une migration appliquée ne doit pas changer.
        private const string OrganisationDeSession = "NULLIF(current_setting('app.organisation_id', true), '')::uuid";

        private static void ActiverIsolation(MigrationBuilder migrationBuilder)
        {
            Isoler(migrationBuilder, "organisations", "id");
            foreach (var table in new[] { "sites", "membres", "roles_attribues" })
            {
                Isoler(migrationBuilder, table, "organisation_id");
            }
        }

        private static void Isoler(MigrationBuilder migrationBuilder, string table, string colonne)
        {
            migrationBuilder.Sql($"ALTER TABLE identite.{table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE identite.{table} FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                $"CREATE POLICY isolation_organisation ON identite.{table} " +
                $"USING ({colonne} = {OrganisationDeSession}) WITH CHECK ({colonne} = {OrganisationDeSession});");
        }

        // Rôle de l'API : lecture et écriture des données, aucun droit sur le schéma ; lecture de
        // l'historique des migrations pour la sonde de disponibilité.
        private static void AccorderRoleApplicatif(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA identite TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT ON identite.__ef_migrations TO gestionsite_app;");
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON identite.organisations, identite.sites, " +
                "identite.membres, identite.roles_attribues TO gestionsite_app;");
        }
    }
}
