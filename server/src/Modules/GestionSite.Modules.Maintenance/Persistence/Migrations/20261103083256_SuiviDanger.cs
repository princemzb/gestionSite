using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuiviDanger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "alerte_id",
                schema: "maintenance",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fin",
                schema: "maintenance",
                table: "alertes_danger",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fin_commentaire",
                schema: "maintenance",
                table: "alertes_danger",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fin_le",
                schema: "maintenance",
                table: "alertes_danger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fin_motif",
                schema: "maintenance",
                table: "alertes_danger",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fin_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pris_en_compte_le",
                schema: "maintenance",
                table: "alertes_danger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pris_en_compte_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rappels_securite_depuis",
                schema: "maintenance",
                table: "alertes_danger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "securite_action",
                schema: "maintenance",
                table: "alertes_danger",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "securite_commentaire",
                schema: "maintenance",
                table: "alertes_danger",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "securite_declaree_le",
                schema: "maintenance",
                table: "alertes_danger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "securite_declaree_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "securite_verifiee_le",
                schema: "maintenance",
                table: "alertes_danger",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "securite_verifiee_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "maintenance",
                table: "alertes_danger",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organisation_id_compte_id_alerte_id",
                schema: "maintenance",
                table: "notifications",
                columns: new[] { "organisation_id", "compte_id", "alerte_id" },
                filter: "alerte_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_alertes_danger_en_cours",
                schema: "maintenance",
                table: "alertes_danger",
                columns: new[] { "organisation_id", "site_id" },
                filter: "fin IS NULL");

            // Suivi du danger (US-10, S10-04) : le rôle applicatif ne modifie que les étapes du suivi (ni l'organisation, ni le
            // site, ni le dossier, ni l'origine de l'alerte) ; aucune suppression.
            migrationBuilder.Sql(
                "GRANT UPDATE (pris_en_compte_le, pris_en_compte_par_compte_id, rappels_securite_depuis, securite_action, securite_commentaire, " +
                "securite_declaree_le, securite_declaree_par_compte_id, securite_verifiee_le, securite_verifiee_par_compte_id, fin, fin_motif, " +
                "fin_commentaire, fin_le, fin_par_compte_id) ON maintenance.alertes_danger TO gestionsite_app;");

            // Planificateur : un danger en cours relance ses alertes même sur un dossier clos (la clôture ne lève pas le danger).
            // Même fonction (propriétaire, droits et garde-fous de la migration RappelsPlanificateur), élargie aux organisations
            // qui ont un danger en cours ; la politique réservée au propriétaire s'ouvre de la même façon sur les alertes.
            migrationBuilder.Sql(OrganisationsAPlanifier(avecDangers: true));
            migrationBuilder.Sql(
                "CREATE POLICY planificateur_organisations ON maintenance.alertes_danger FOR SELECT TO CURRENT_USER " +
                "USING (current_setting('app.planificateur', true) = 'on' AND session_user <> current_user);");
        }

        // Identifiants seulement : organisations ayant un dossier ouvert (statuts de Signalement.StatutsOuverts) ou un danger en cours.
        private static string OrganisationsAPlanifier(bool avecDangers) =>
            "CREATE OR REPLACE FUNCTION maintenance.organisations_a_planifier() RETURNS SETOF uuid " +
            "LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$ " +
            "BEGIN " +
            "PERFORM set_config('app.planificateur', 'on', true); " +
            "RETURN QUERY SELECT s.organisation_id FROM maintenance.signalements s " +
            "WHERE s.statut IN ('AQualifier', 'Affecte', 'EnCours', 'EnAttente')" +
            (avecDangers ? " UNION SELECT a.organisation_id FROM maintenance.alertes_danger a WHERE a.fin IS NULL; " : " GROUP BY s.organisation_id; ") +
            "PERFORM set_config('app.planificateur', '', true); " +
            "END $$;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY planificateur_organisations ON maintenance.alertes_danger;");
            migrationBuilder.Sql(OrganisationsAPlanifier(avecDangers: false));
            migrationBuilder.Sql("REVOKE UPDATE ON maintenance.alertes_danger FROM gestionsite_app;");

            migrationBuilder.DropIndex(
                name: "ix_notifications_organisation_id_compte_id_alerte_id",
                schema: "maintenance",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_alertes_danger_en_cours",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "alerte_id",
                schema: "maintenance",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "fin",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "fin_commentaire",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "fin_le",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "fin_motif",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "fin_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "pris_en_compte_le",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "pris_en_compte_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "rappels_securite_depuis",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_action",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_commentaire",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_declaree_le",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_declaree_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_verifiee_le",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "securite_verifiee_par_compte_id",
                schema: "maintenance",
                table: "alertes_danger");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "maintenance",
                table: "alertes_danger");
        }
    }
}
