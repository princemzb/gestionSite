using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialeOrganisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identite");

            migrationBuilder.CreateTable(
                name: "organisations",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisations", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organisations",
                schema: "identite");
        }
    }
}
