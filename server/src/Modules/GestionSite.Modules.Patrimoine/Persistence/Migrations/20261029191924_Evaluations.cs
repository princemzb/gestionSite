using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Evaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_composants_id_organisation_id",
                schema: "patrimoine",
                table: "composants",
                columns: new[] { "id", "organisation_id" });

            migrationBuilder.CreateTable(
                name: "evaluations",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    composant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corps_etat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grille_version = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<string>(type: "jsonb", nullable: false),
                    mesures = table.Column<string>(type: "jsonb", nullable: false),
                    etat = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bon = table.Column<int>(type: "integer", nullable: true),
                    satisfaisant = table.Column<int>(type: "integer", nullable: true),
                    degrade = table.Column<int>(type: "integer", nullable: true),
                    mauvais = table.Column<int>(type: "integer", nullable: true),
                    critique = table.Column<int>(type: "integer", nullable: true),
                    remarque = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evaluateur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evalue_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluations", x => x.id);
                    table.UniqueConstraint("ak_evaluations_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.CheckConstraint("ck_evaluations_etat", "(etat IS NOT NULL AND bon IS NULL AND satisfaisant IS NULL AND degrade IS NULL AND mauvais IS NULL AND critique IS NULL) OR (etat IS NULL AND bon >= 0 AND satisfaisant >= 0 AND degrade >= 0 AND mauvais >= 0 AND critique >= 0 AND bon + satisfaisant + degrade + mauvais + critique >= 1)");
                    table.CheckConstraint("ck_evaluations_grille", "grille_version >= 1");
                    table.ForeignKey(
                        name: "fk_evaluations_composants_composant_id_organisation_id",
                        columns: x => new { x.composant_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "composants",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "photos_evaluations",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rang = table.Column<int>(type: "integer", nullable: false),
                    cle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    taille = table.Column<int>(type: "integer", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos_evaluations", x => x.id);
                    table.CheckConstraint("ck_photos_evaluations_rang", "rang BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_photos_evaluations_evaluations_evaluation_id_organisation_id",
                        columns: x => new { x.evaluation_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "evaluations",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_commande",
                schema: "patrimoine",
                table: "evaluations",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_composant_id_organisation_id",
                schema: "patrimoine",
                table: "evaluations",
                columns: new[] { "composant_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_organisation_id_site_id_composant_id_evalue_le",
                schema: "patrimoine",
                table: "evaluations",
                columns: new[] { "organisation_id", "site_id", "composant_id", "evalue_le" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_evaluations_evaluation_id_organisation_id",
                schema: "patrimoine",
                table: "photos_evaluations",
                columns: new[] { "evaluation_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_evaluations_evaluation_id_rang",
                schema: "patrimoine",
                table: "photos_evaluations",
                columns: new[] { "evaluation_id", "rang" },
                unique: true);

            // Évaluations et leurs photos (US-11, S11-02) : ajout seul, isolées par organisation.
            migrationBuilder.IsolerParOrganisation("patrimoine", "evaluations");
            migrationBuilder.IsolerParOrganisation("patrimoine", "photos_evaluations");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON patrimoine.evaluations, patrimoine.photos_evaluations TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.evaluations, patrimoine.photos_evaluations FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "photos_evaluations",
                schema: "patrimoine");

            migrationBuilder.DropTable(
                name: "evaluations",
                schema: "patrimoine");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_composants_id_organisation_id",
                schema: "patrimoine",
                table: "composants");
        }
    }
}
