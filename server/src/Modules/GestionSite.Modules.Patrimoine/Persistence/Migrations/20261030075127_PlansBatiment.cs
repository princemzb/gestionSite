using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlansBatiment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans_batiment",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    niveau_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans_batiment", x => x.id);
                    table.UniqueConstraint("ak_plans_batiment_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.ForeignKey(
                        name: "fk_plans_batiment_batiments_batiment_id_organisation_id",
                        columns: x => new { x.batiment_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "batiments",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plans_batiment_niveaux_niveau_id_organisation_id",
                        columns: x => new { x.niveau_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "niveaux",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "versions_plan_batiment",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<int>(type: "integer", nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contenu = table.Column<string>(type: "jsonb", nullable: false),
                    avertissements = table.Column<string>(type: "jsonb", nullable: false),
                    motif = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    restaure_de = table.Column<int>(type: "integer", nullable: true),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versions_plan_batiment", x => x.id);
                    table.CheckConstraint("ck_versions_plan_batiment_restauration", "restaure_de IS NULL OR (restaure_de >= 1 AND restaure_de < version)");
                    table.CheckConstraint("ck_versions_plan_batiment_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_versions_plan_batiment_plans_batiment_plan_id_organisation_",
                        columns: x => new { x.plan_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "plans_batiment",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plans_batiment_batiment_id_organisation_id",
                schema: "patrimoine",
                table: "plans_batiment",
                columns: new[] { "batiment_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_batiment_niveau",
                schema: "patrimoine",
                table: "plans_batiment",
                columns: new[] { "organisation_id", "batiment_id", "niveau_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_plans_batiment_niveau_id_organisation_id",
                schema: "patrimoine",
                table: "plans_batiment",
                columns: new[] { "niveau_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_batiment_organisation_id_site_id",
                schema: "patrimoine",
                table: "plans_batiment",
                columns: new[] { "organisation_id", "site_id" });

            migrationBuilder.CreateIndex(
                name: "ix_versions_plan_batiment_commande",
                schema: "patrimoine",
                table: "versions_plan_batiment",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versions_plan_batiment_plan_id_organisation_id",
                schema: "patrimoine",
                table: "versions_plan_batiment",
                columns: new[] { "plan_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_versions_plan_batiment_version",
                schema: "patrimoine",
                table: "versions_plan_batiment",
                columns: new[] { "organisation_id", "plan_id", "version" },
                unique: true);

            // Seconde barrière (ADR-10) ; versions en ajout seul : ni modification ni suppression par l'application.
            migrationBuilder.IsolerParOrganisation("patrimoine", "plans_batiment");
            migrationBuilder.IsolerParOrganisation("patrimoine", "versions_plan_batiment");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON patrimoine.plans_batiment, patrimoine.versions_plan_batiment TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.plans_batiment, patrimoine.versions_plan_batiment FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "versions_plan_batiment",
                schema: "patrimoine");

            migrationBuilder.DropTable(
                name: "plans_batiment",
                schema: "patrimoine");
        }
    }
}
