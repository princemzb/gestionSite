using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClassementErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "categorie",
                schema: "patrimoine",
                table: "batiments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "derniere_visite_commission",
                schema: "patrimoine",
                table: "batiments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "effectif_declare",
                schema: "patrimoine",
                table: "batiments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "erp",
                schema: "patrimoine",
                table: "batiments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "locaux_sommeil",
                schema: "patrimoine",
                table: "batiments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "monument_historique",
                schema: "patrimoine",
                table: "batiments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "permis_avant1997",
                schema: "patrimoine",
                table: "batiments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type_etablissement",
                schema: "patrimoine",
                table: "batiments",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "types_secondaires",
                schema: "patrimoine",
                table: "batiments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "etablissements",
                schema: "patrimoine",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salaries_presents = table.Column<bool>(type: "boolean", nullable: false),
                    modifie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etablissements", x => x.site_id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_batiments_classement",
                schema: "patrimoine",
                table: "batiments",
                sql: "(erp IS NULL AND permis_avant1997 IS NULL AND type_etablissement IS NULL AND categorie IS NULL) OR (erp AND permis_avant1997 IS NOT NULL AND type_etablissement IS NOT NULL AND categorie BETWEEN 1 AND 5) OR (NOT erp AND permis_avant1997 IS NOT NULL AND type_etablissement IS NULL AND types_secondaires IS NULL AND categorie IS NULL AND effectif_declare IS NULL AND NOT locaux_sommeil AND derniere_visite_commission IS NULL)");

            // Données du site pour le préventif (US-12, S12-01) : une ligne par site, modifiable, isolée par organisation.
            migrationBuilder.IsolerParOrganisation("patrimoine", "etablissements");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON patrimoine.etablissements TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.etablissements FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "etablissements",
                schema: "patrimoine");

            migrationBuilder.DropCheckConstraint(
                name: "ck_batiments_classement",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "categorie",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "derniere_visite_commission",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "effectif_declare",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "erp",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "locaux_sommeil",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "monument_historique",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "permis_avant1997",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "type_etablissement",
                schema: "patrimoine",
                table: "batiments");

            migrationBuilder.DropColumn(
                name: "types_secondaires",
                schema: "patrimoine",
                table: "batiments");
        }
    }
}
