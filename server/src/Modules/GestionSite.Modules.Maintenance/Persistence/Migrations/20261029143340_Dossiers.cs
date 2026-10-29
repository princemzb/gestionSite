using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dossiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "contact_declarant",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "commentaire_classement",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "composant_id",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "declarant_compte_id",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "doublon_de",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motif_classement",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origine",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Public");

            migrationBuilder.AddColumn<string>(
                name: "priorite",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "qualifie_le",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statut",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "AQualifier");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "maintenance",
                table: "signalements",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "zone_id",
                schema: "maintenance",
                table: "signalements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "journal_dossiers",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dossier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_dossiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_dossiers_signalements_dossier_id_organisation_id",
                        columns: x => new { x.dossier_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "signalements",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signalements_doublon_de_organisation_id",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "doublon_de", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_site_id_statut",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "site_id", "statut" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_dossiers_dossier_id_organisation_id",
                schema: "maintenance",
                table: "journal_dossiers",
                columns: new[] { "dossier_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_dossiers_organisation_id_dossier_id_id",
                schema: "maintenance",
                table: "journal_dossiers",
                columns: new[] { "organisation_id", "dossier_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_signalements_signalements_doublon_de_organisation_id",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "doublon_de", "organisation_id" },
                principalSchema: "maintenance",
                principalTable: "signalements",
                principalColumns: new[] { "id", "organisation_id" },
                onDelete: ReferentialAction.Restrict);

            // Traitement des dossiers (US-09) : mises à jour du dossier par l'API ; journal en ajout seul, isolé par organisation.
            migrationBuilder.IsolerParOrganisation("maintenance", "journal_dossiers");
            migrationBuilder.Sql("GRANT UPDATE ON maintenance.signalements TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.journal_dossiers TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.journal_dossiers FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE UPDATE ON maintenance.signalements FROM gestionsite_app;");
            migrationBuilder.DropForeignKey(
                name: "fk_signalements_signalements_doublon_de_organisation_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropTable(
                name: "journal_dossiers",
                schema: "maintenance");

            migrationBuilder.DropIndex(
                name: "ix_signalements_doublon_de_organisation_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropIndex(
                name: "ix_signalements_organisation_id_site_id_statut",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "commentaire_classement",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "composant_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "declarant_compte_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "doublon_de",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "motif_classement",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "origine",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "priorite",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "qualifie_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "statut",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "zone_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.AlterColumn<string>(
                name: "contact_declarant",
                schema: "maintenance",
                table: "signalements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
