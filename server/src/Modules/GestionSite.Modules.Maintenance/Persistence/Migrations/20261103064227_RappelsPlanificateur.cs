using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RappelsPlanificateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "en_attente_depuis",
                schema: "maintenance",
                table: "interventions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reports_reexamen",
                schema: "maintenance",
                table: "interventions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Planificateur (US-10, S10-02) : il n'a ni organisation ni compte. Cette fonction lui donne seulement les
            // identifiants des organisations qui ont un dossier ouvert ; chacune est ensuite traitée sous sa propre RLS.
            // Elle s'exécute avec les droits de son propriétaire (le rôle des migrations, soumis lui aussi à la RLS forcée) :
            // la politique ci-dessous, réservée à ce rôle, ne s'ouvre que pendant l'exécution de la fonction (variable
            // app.planificateur posée par sa clause SET, rétablie à la sortie) et seulement pour une session ouverte par un autre
            // rôle (session_user ≠ current_user : une connexion directe du propriétaire reste fermée, même variable posée).
            // Le rôle applicatif ne peut ni en bénéficier (politique « TO » le propriétaire) ni lire autre chose que ces
            // identifiants ; chemin de recherche figé.
            migrationBuilder.Sql(
                "CREATE FUNCTION maintenance.organisations_a_planifier() RETURNS SETOF uuid " +
                "LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET app.planificateur = 'on' AS $$ " +
                "SELECT DISTINCT s.organisation_id FROM maintenance.signalements s " +
                "WHERE s.statut IN ('AQualifier', 'Affecte', 'EnCours', 'EnAttente') $$;");
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION maintenance.organisations_a_planifier() FROM PUBLIC;");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION maintenance.organisations_a_planifier() TO gestionsite_app;");
            migrationBuilder.Sql(
                "CREATE POLICY planificateur_organisations ON maintenance.signalements FOR SELECT TO CURRENT_USER " +
                "USING (current_setting('app.planificateur', true) = 'on' AND session_user <> current_user);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY planificateur_organisations ON maintenance.signalements;");
            migrationBuilder.Sql("DROP FUNCTION maintenance.organisations_a_planifier();");

            migrationBuilder.DropColumn(
                name: "en_attente_depuis",
                schema: "maintenance",
                table: "interventions");

            migrationBuilder.DropColumn(
                name: "reports_reexamen",
                schema: "maintenance",
                table: "interventions");
        }
    }
}
