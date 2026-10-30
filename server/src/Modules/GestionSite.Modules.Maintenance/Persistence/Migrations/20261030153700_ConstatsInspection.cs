using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConstatsInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_alertes_danger_signalement_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.AddColumn<string>(
                name: "cle_constat",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "controle_id",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_id",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "constat_id",
                schema: "maintenance",
                table: "alertes_danger",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "constats_controles",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    controle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rang = table.Column<int>(type: "integer", nullable: false),
                    libelle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cle = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rattache = table.Column<bool>(type: "boolean", nullable: false),
                    danger = table.Column<bool>(type: "boolean", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_constats_controles", x => x.id);
                    table.ForeignKey(
                        name: "fk_constats_controles_controles_preventifs_controle_id_organis",
                        columns: x => new { x.controle_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "controles_preventifs",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_constats_controles_signalements_dossier_id_organisation_id",
                        columns: x => new { x.dossier_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "signalements",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_site_id_cle_constat",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "site_id", "cle_constat" },
                filter: "cle_constat IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alertes_danger_constat_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger",
                columns: new[] { "constat_id", "organisation_id" },
                unique: true,
                filter: "constat_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alertes_danger_signalement_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger",
                columns: new[] { "signalement_id", "organisation_id" },
                unique: true,
                filter: "constat_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_constats_controles_controle_id_organisation_id_rang",
                schema: "maintenance",
                table: "constats_controles",
                columns: new[] { "controle_id", "organisation_id", "rang" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_constats_controles_dossier_id_organisation_id",
                schema: "maintenance",
                table: "constats_controles",
                columns: new[] { "dossier_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_constats_controles_organisation_id_occurrence_id",
                schema: "maintenance",
                table: "constats_controles",
                columns: new[] { "organisation_id", "occurrence_id" });

            // Constats des contrôles (US-12, S12-05) : ajout seul, isolés par organisation. Signalements et alertes gardent leurs droits.
            migrationBuilder.IsolerParOrganisation("maintenance", "constats_controles");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.constats_controles TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.constats_controles FROM gestionsite_app;");
            // Alertes des constats retirées avant le retour à une alerte par dossier (RLS levée le temps de la requête).
            migrationBuilder.Sql("ALTER TABLE maintenance.alertes_danger NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DELETE FROM maintenance.alertes_danger WHERE constat_id IS NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE maintenance.alertes_danger FORCE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "constats_controles",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "ix_signalements_organisation_id_site_id_cle_constat",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropIndex(
                name: "ix_alertes_danger_constat_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropIndex(
                name: "ix_alertes_danger_signalement_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "cle_constat",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "controle_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "occurrence_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "constat_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.CreateIndex(
                name: "ix_alertes_danger_signalement_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger",
                columns: new[] { "signalement_id", "organisation_id" },
                unique: true);
        }
    }
}
