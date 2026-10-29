using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Clotures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cloture_le",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rouvert_le",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actions",
                schema: "maintenance",
                table: "interventions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cloturee_le",
                schema: "maintenance",
                table: "interventions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "etat_final",
                schema: "maintenance",
                table: "interventions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "photo_exigee",
                schema: "maintenance",
                table: "interventions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_interventions_id_organisation_id",
                schema: "maintenance",
                table: "interventions",
                columns: new[] { "id", "organisation_id" });

            migrationBuilder.CreateTable(
                name: "photos_intervention",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intervention_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rang = table.Column<int>(type: "integer", nullable: false),
                    cle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    taille = table.Column<int>(type: "integer", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos_intervention", x => x.id);
                    table.CheckConstraint("ck_photos_intervention_rang", "rang BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_photos_intervention_interventions_intervention_id_organisat",
                        columns: x => new { x.intervention_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "interventions",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_photos_intervention_intervention_id_organisation_id_rang",
                schema: "maintenance",
                table: "photos_intervention",
                columns: new[] { "intervention_id", "organisation_id", "rang" },
                unique: true);

            // Photos après intervention (US-09, S09-04) : ajout seul par l'API, isolées par organisation.
            migrationBuilder.IsolerParOrganisation("maintenance", "photos_intervention");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.photos_intervention TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.photos_intervention FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "photos_intervention",
                schema: "maintenance");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_interventions_id_organisation_id",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "cloture_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "rouvert_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "actions",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "cloturee_le",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "etat_final",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "photo_exigee",
                schema: "maintenance",
                table: "interventions");
        }
    }
}
