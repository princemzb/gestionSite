using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexListeSignalements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_signalements_organisation_id_site_id_recu_le",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_site_id_id",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "site_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_signalements_organisation_id_site_id_id",
                schema: "maintenance",
                table: "signalements");

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_site_id_recu_le",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "site_id", "recu_le" });
        }
    }
}
