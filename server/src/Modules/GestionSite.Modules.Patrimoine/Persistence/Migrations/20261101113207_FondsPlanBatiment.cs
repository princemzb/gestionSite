using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FondsPlanBatiment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fonds_plan_batiment",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_contenu = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    largeur = table.Column<int>(type: "integer", nullable: false),
                    hauteur = table.Column<int>(type: "integer", nullable: false),
                    taille_maitre = table.Column<int>(type: "integer", nullable: false),
                    largeur_affichage = table.Column<int>(type: "integer", nullable: false),
                    hauteur_affichage = table.Column<int>(type: "integer", nullable: false),
                    taille_affichage = table.Column<int>(type: "integer", nullable: false),
                    cle_maitre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cle_affichage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fonds_plan_batiment", x => x.id);
                    table.CheckConstraint("ck_fonds_plan_batiment_tailles", "taille_maitre > 0 AND taille_affichage > 0");
                    table.CheckConstraint("ck_fonds_plan_batiment_type", "type_contenu IN ('image/png', 'image/jpeg')");
                    table.ForeignKey(
                        name: "fk_fonds_plan_batiment_plans_batiment_plan_id_organisation_id",
                        columns: x => new { x.plan_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "plans_batiment",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fonds_plan_batiment_commande",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fonds_plan_batiment_plan_id_organisation_id",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                columns: new[] { "plan_id", "organisation_id" });

            // Seconde barrière (ADR-10) ; fonds en ajout seul : ni modification ni suppression par l'application.
            migrationBuilder.IsolerParOrganisation("patrimoine", "fonds_plan_batiment");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON patrimoine.fonds_plan_batiment TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.fonds_plan_batiment FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "fonds_plan_batiment",
                schema: "patrimoine");
        }
    }
}
