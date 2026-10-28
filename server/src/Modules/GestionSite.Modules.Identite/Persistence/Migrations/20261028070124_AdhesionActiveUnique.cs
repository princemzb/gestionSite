using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdhesionActiveUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_membres_organisation_id_compte_id",
                schema: "identite",
                table: "membres");

            migrationBuilder.CreateIndex(
                name: "ix_membres_organisation_id_compte_id",
                schema: "identite",
                table: "membres",
                columns: new[] { "organisation_id", "compte_id" },
                unique: true,
                filter: "revoque_le IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_membres_organisation_id_compte_id",
                schema: "identite",
                table: "membres");

            migrationBuilder.CreateIndex(
                name: "ix_membres_organisation_id_compte_id",
                schema: "identite",
                table: "membres",
                columns: new[] { "organisation_id", "compte_id" },
                unique: true);
        }
    }
}
