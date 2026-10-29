using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Grilles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grilles",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corps_etat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    criteres = table.Column<string>(type: "jsonb", nullable: false),
                    mesures = table.Column<string>(type: "jsonb", nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grilles", x => x.id);
                    table.CheckConstraint("ck_grilles_version", "version >= 2");
                });

            migrationBuilder.CreateIndex(
                name: "ix_grilles_version",
                schema: "patrimoine",
                table: "grilles",
                columns: new[] { "organisation_id", "site_id", "corps_etat", "version" },
                unique: true);

            // Versions adaptées des grilles par site (US-11, S11-01) : ajout seul, isolées par organisation.
            migrationBuilder.IsolerParOrganisation("patrimoine", "grilles");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON patrimoine.grilles TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.grilles FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "grilles",
                schema: "patrimoine");
        }
    }
}
