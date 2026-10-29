using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attente_depuis",
                schema: "maintenance",
                table: "signalements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "duree_attente",
                schema: "maintenance",
                table: "signalements",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<DateOnly>(
                name: "reexamen_le",
                schema: "maintenance",
                table: "signalements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commentaire_attente",
                schema: "maintenance",
                table: "interventions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motif_attente",
                schema: "maintenance",
                table: "interventions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "reexamen_le",
                schema: "maintenance",
                table: "interventions",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attente_depuis",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "duree_attente",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "reexamen_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.DropColumn(
                name: "commentaire_attente",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "motif_attente",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "reexamen_le",
                schema: "maintenance",
                table: "interventions");
        }
    }
}
