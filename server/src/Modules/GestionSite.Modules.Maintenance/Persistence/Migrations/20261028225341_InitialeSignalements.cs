using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialeSignalements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maintenance");

            migrationBuilder.CreateTable(
                name: "signalements",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    emplacement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    danger = table.Column<bool>(type: "boolean", nullable: false),
                    activite_bloquee = table.Column<bool>(type: "boolean", nullable: false),
                    nom_declarant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_declarant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recu_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signalements", x => x.id);
                    table.UniqueConstraint("ak_signalements_id_organisation_id", x => new { x.id, x.organisation_id });
                });

            migrationBuilder.CreateTable(
                name: "alertes_danger",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signalement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alertes_danger", x => x.id);
                    table.ForeignKey(
                        name: "fk_alertes_danger_signalements_signalement_id_organisation_id",
                        columns: x => new { x.signalement_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "signalements",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alertes_danger_signalement_id_organisation_id",
                schema: "maintenance",
                table: "alertes_danger",
                columns: new[] { "signalement_id", "organisation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_commande_id",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signalements_organisation_id_site_id_recu_le",
                schema: "maintenance",
                table: "signalements",
                columns: new[] { "organisation_id", "site_id", "recu_le" });

            foreach (var table in new[] { "signalements", "alertes_danger" })
            {
                Isoler(migrationBuilder, table);
            }

            // Rôle de l'API : lecture et ajout (le traitement, US-09, ajoutera ce qu'il faut) ; aucun droit sur
            // le schéma ; lecture de l'historique des migrations pour la sonde de disponibilité.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA maintenance TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT ON maintenance.__ef_migrations TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.signalements, maintenance.alertes_danger TO gestionsite_app;");
        }

        // Seconde barrière (ADR-10), comme Identité : sans organisation de session, aucune ligne visible ni écrite.
        private static void Isoler(MigrationBuilder migrationBuilder, string table)
        {
            const string organisationDeSession = "NULLIF(current_setting('app.organisation_id', true), '')::uuid";
            migrationBuilder.Sql($"ALTER TABLE maintenance.{table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE maintenance.{table} FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                $"CREATE POLICY isolation_organisation ON maintenance.{table} " +
                $"USING (organisation_id = {organisationDeSession}) WITH CHECK (organisation_id = {organisationDeSession});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.signalements, maintenance.alertes_danger FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE SELECT ON maintenance.__ef_migrations FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE USAGE ON SCHEMA maintenance FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "alertes_danger",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "signalements",
                schema: "maintenance");
        }
    }
}
