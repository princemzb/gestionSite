using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CentreNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    objet = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    objet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lue_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organisation_id_compte_id_cle",
                schema: "maintenance",
                table: "notifications",
                columns: new[] { "organisation_id", "compte_id", "cle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organisation_id_compte_id_id",
                schema: "maintenance",
                table: "notifications",
                columns: new[] { "organisation_id", "compte_id", "id" });

            // Centre de notifications (US-10, S10-01) : isolé par organisation (RLS forcée). Seconde barrière propre au centre :
            // lire et marquer lue sont réservés au destinataire (compte de la requête) ; l'écriture reste ouverte à l'auteur de
            // l'événement, au dépôt public sans compte et plus tard au planificateur (S10-02), dans l'organisation seulement.
            migrationBuilder.IsolerParOrganisation("maintenance", "notifications");
            migrationBuilder.Sql(
                "CREATE POLICY destinataire_seul_lecture ON maintenance.notifications AS RESTRICTIVE FOR SELECT " +
                "USING (compte_id = NULLIF(current_setting('app.compte_id', true), '')::uuid);");
            migrationBuilder.Sql(
                "CREATE POLICY destinataire_seul_marquage ON maintenance.notifications AS RESTRICTIVE FOR UPDATE " +
                "USING (compte_id = NULLIF(current_setting('app.compte_id', true), '')::uuid) " +
                "WITH CHECK (compte_id = NULLIF(current_setting('app.compte_id', true), '')::uuid);");
            // Rôle applicatif minimal : ni suppression, ni modification d'autre chose que la date de lecture.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.notifications TO gestionsite_app;");
            migrationBuilder.Sql("GRANT UPDATE (lue_le) ON maintenance.notifications TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.notifications FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "maintenance");
        }
    }
}
