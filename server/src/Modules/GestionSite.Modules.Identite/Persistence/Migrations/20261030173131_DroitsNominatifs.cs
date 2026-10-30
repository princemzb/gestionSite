using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DroitsNominatifs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "droits_apres",
                schema: "identite",
                table: "journal_habilitations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "droits_avant",
                schema: "identite",
                table: "journal_habilitations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "droits_nominatifs",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    droit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    accorde_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_droits_nominatifs", x => x.id);
                    table.CheckConstraint("ck_droits_nominatifs_liste_blanche", "droit IN ('ModifierPlans')");
                    table.ForeignKey(
                        name: "fk_droits_nominatifs_membres_membre_id_organisation_id",
                        columns: x => new { x.membre_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "membres",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_droits_nominatifs_sites_site_id_organisation_id",
                        columns: x => new { x.site_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_droits_nominatifs_membre_id_organisation_id_droit_site_id",
                schema: "identite",
                table: "droits_nominatifs",
                columns: new[] { "membre_id", "organisation_id", "droit", "site_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_droits_nominatifs_site_id_organisation_id",
                schema: "identite",
                table: "droits_nominatifs",
                columns: new[] { "site_id", "organisation_id" });

            // Seconde barrière (ADR-10) ; lecture, ajout et retrait par l'API, jamais de modification.
            migrationBuilder.IsolerParOrganisation("identite", "droits_nominatifs");
            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON identite.droits_nominatifs TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON identite.droits_nominatifs FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "droits_nominatifs",
                schema: "identite");

            migrationBuilder.DropColumn(
                name: "droits_apres",
                schema: "identite",
                table: "journal_habilitations");

            migrationBuilder.DropColumn(
                name: "droits_avant",
                schema: "identite",
                table: "journal_habilitations");
        }
    }
}
