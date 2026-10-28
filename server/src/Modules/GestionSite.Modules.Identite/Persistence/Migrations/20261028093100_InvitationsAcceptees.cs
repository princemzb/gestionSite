using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvitationsAcceptees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepte_le",
                schema: "identite",
                table: "membres",
                type: "timestamp with time zone",
                nullable: true);

            // Adhésions antérieures : réputées acceptées. Migration de données sous RLS forcée : levée le temps
            // de l'instruction (sinon le propriétaire, sans organisation, ne verrait aucune ligne).
            migrationBuilder.Sql("ALTER TABLE identite.membres NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("UPDATE identite.membres SET accepte_le = cree_le WHERE accepte_le IS NULL;");
            migrationBuilder.Sql("ALTER TABLE identite.membres FORCE ROW LEVEL SECURITY;");

            // Droits « par compte » sur les sites et rôles : adhésion acceptée seulement. Les invitations en
            // attente restent visibles du titulaire (membres, nom de l'organisation) pour être acceptées.
            RemplacerPolitiques(migrationBuilder, "AND m.accepte_le IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemplacerPolitiques(migrationBuilder, string.Empty);
            migrationBuilder.DropColumn(
                name: "accepte_le",
                schema: "identite",
                table: "membres");
        }

        // Nom figé (CompteCourant.VariableSession) : une migration appliquée ne doit pas changer.
        private const string CompteDeSession = "NULLIF(current_setting('app.compte_id', true), '')::uuid";

        private static void RemplacerPolitiques(MigrationBuilder migrationBuilder, string condition)
        {
            migrationBuilder.Sql("DROP POLICY lecture_par_compte ON identite.roles_attribues;");
            migrationBuilder.Sql("DROP POLICY lecture_par_compte ON identite.sites;");
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.roles_attribues FOR SELECT USING (EXISTS (" +
                $"SELECT 1 FROM identite.membres m WHERE m.compte_id = {CompteDeSession} AND m.revoque_le IS NULL {condition} " +
                "AND m.id = roles_attribues.membre_id));");
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.sites FOR SELECT USING (EXISTS (" +
                "SELECT 1 FROM identite.membres m JOIN identite.roles_attribues r ON r.membre_id = m.id " +
                $"WHERE m.compte_id = {CompteDeSession} AND m.revoque_le IS NULL {condition} AND r.organisation_id = sites.organisation_id " +
                "AND (r.role = 'Administrateur' OR r.site_id = sites.id)));");
        }
    }
}
