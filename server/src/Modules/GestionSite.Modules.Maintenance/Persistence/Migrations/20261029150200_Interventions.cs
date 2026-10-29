using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Interventions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "affecte_le",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pris_en_charge_le",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "interventions",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    titre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    intervenant_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prestataire = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    referent_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    statut = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    demarree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interventions", x => x.id);
                    table.ForeignKey(
                        name: "fk_interventions_signalements_dossier_id_organisation_id",
                        columns: x => new { x.dossier_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "signalements",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interventions_dossier_id_organisation_id_numero",
                schema: "maintenance",
                table: "interventions",
                columns: new[] { "dossier_id", "organisation_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interventions_organisation_id_site_id_statut",
                schema: "maintenance",
                table: "interventions",
                columns: new[] { "organisation_id", "site_id", "statut" });

            // Interventions (US-09, S09-02) : créées, démarrées puis clôturées par l'API ; isolées par organisation.
            migrationBuilder.IsolerParOrganisation("maintenance", "interventions");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON maintenance.interventions TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.interventions FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "interventions",
                schema: "maintenance");

            migrationBuilder.DropColumn(
                name: "affecte_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "pris_en_charge_le",
                schema: "maintenance",
                table: "signalements");
        }
    }
}
