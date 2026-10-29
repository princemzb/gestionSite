using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhotosSignalement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "photos_signalement",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signalement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rang = table.Column<int>(type: "integer", nullable: false),
                    cle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    taille = table.Column<int>(type: "integer", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos_signalement", x => x.id);
                    table.CheckConstraint("ck_photos_signalement_rang", "rang BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_photos_signalement_signalements_signalement_id_organisation",
                        columns: x => new { x.signalement_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "signalements",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_photos_signalement_signalement_id_organisation_id_rang",
                schema: "maintenance",
                table: "photos_signalement",
                columns: new[] { "signalement_id", "organisation_id", "rang" },
                unique: true);

            // Mêmes barrières que les signalements (ADR-10).
            migrationBuilder.Sql("ALTER TABLE maintenance.photos_signalement ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE maintenance.photos_signalement FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY isolation_organisation ON maintenance.photos_signalement " +
                "USING (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid) " +
                "WITH CHECK (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.photos_signalement TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.photos_signalement FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "photos_signalement",
                schema: "maintenance");
        }
    }
}
