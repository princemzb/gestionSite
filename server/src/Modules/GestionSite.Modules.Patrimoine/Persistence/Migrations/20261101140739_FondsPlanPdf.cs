using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FondsPlanPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cle_original",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "echelle",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hauteur_page_mm",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "numeric(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "largeur_page_mm",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "numeric(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pages_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "taille_original",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fonds_plan_batiment_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment",
                sql: "(cle_original IS NULL AND taille_original IS NULL AND page_pdf IS NULL AND pages_pdf IS NULL AND largeur_page_mm IS NULL AND hauteur_page_mm IS NULL AND echelle IS NULL) OR (cle_original IS NOT NULL AND taille_original > 0 AND page_pdf BETWEEN 1 AND pages_pdf AND largeur_page_mm > 0 AND hauteur_page_mm > 0 AND (echelle IS NULL OR echelle BETWEEN 1 AND 10000))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fonds_plan_batiment_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "cle_original",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "echelle",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "hauteur_page_mm",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "largeur_page_mm",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "page_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "pages_pdf",
                schema: "patrimoine",
                table: "fonds_plan_batiment");

            migrationBuilder.DropColumn(
                name: "taille_original",
                schema: "patrimoine",
                table: "fonds_plan_batiment");
        }
    }
}
