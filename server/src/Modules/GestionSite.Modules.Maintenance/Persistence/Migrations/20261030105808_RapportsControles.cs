using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RapportsControles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observations",
                schema: "maintenance",
                table: "controles_preventifs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "rapport_cle",
                schema: "maintenance",
                table: "controles_preventifs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rapport_empreinte",
                schema: "maintenance",
                table: "controles_preventifs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rapport_taille",
                schema: "maintenance",
                table: "controles_preventifs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_controles_preventifs_rapport",
                schema: "maintenance",
                table: "controles_preventifs",
                sql: "(rapport_cle IS NULL) = (rapport_taille IS NULL) AND (rapport_cle IS NULL) = (rapport_empreinte IS NULL) AND (rapport_taille IS NULL OR rapport_taille BETWEEN 1 AND 10485760)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_controles_preventifs_rapport",
                schema: "maintenance",
                table: "controles_preventifs");

            migrationBuilder.DropColumn(
                name: "observations",
                schema: "maintenance",
                table: "controles_preventifs");

            migrationBuilder.DropColumn(
                name: "rapport_cle",
                schema: "maintenance",
                table: "controles_preventifs");

            migrationBuilder.DropColumn(
                name: "rapport_empreinte",
                schema: "maintenance",
                table: "controles_preventifs");

            migrationBuilder.DropColumn(
                name: "rapport_taille",
                schema: "maintenance",
                table: "controles_preventifs");
        }
    }
}
