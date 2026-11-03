using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReinitialisationsMotDePasse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reinitialisations",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empreinte = table.Column<byte[]>(type: "bytea", fixedLength: true, maxLength: 32, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expire_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    utilise_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reinitialisations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reinitialisations_comptes_compte_id",
                        column: x => x.compte_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reinitialisations_compte_id",
                schema: "identite",
                table: "reinitialisations",
                column: "compte_id");

            migrationBuilder.CreateIndex(
                name: "ix_reinitialisations_empreinte",
                schema: "identite",
                table: "reinitialisations",
                column: "empreinte",
                unique: true);

            // Liens de réinitialisation : données hors organisation (comme les liens d'activation), lus par empreinte seulement.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON identite.reinitialisations TO gestionsite_app;");

            // Lien de réinitialisation créé par un administrateur (US-31) : le compte ne doit être lié à aucune autre organisation,
            // dont les adhésions sont invisibles sous la RLS. Cette fonction ne rend qu'un booléen. Même construction que
            // maintenance.organisations_a_planifier : droits du propriétaire (rôle des migrations, soumis lui aussi à la RLS forcée),
            // politique réservée à ce rôle et ouverte seulement pendant l'exécution de la fonction (variable posée localement, effacée
            // avant de rendre la main) et pour une session d'un autre rôle (une connexion directe du propriétaire reste fermée) ;
            // le rôle applicatif ne lit rien d'autre ; chemin de recherche figé.
            migrationBuilder.Sql(
                "CREATE FUNCTION identite.compte_lie_a_une_autre_organisation(p_compte uuid, p_organisation uuid) RETURNS boolean " +
                "LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$ " +
                "DECLARE lie boolean; " +
                "BEGIN " +
                "PERFORM set_config('app.verification_compte', 'on', true); " +
                "SELECT EXISTS (SELECT 1 FROM identite.membres m WHERE m.compte_id = p_compte " +
                "AND m.organisation_id <> p_organisation AND m.revoque_le IS NULL) INTO lie; " +
                "PERFORM set_config('app.verification_compte', '', true); " +
                "RETURN lie; " +
                "END $$;");
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION identite.compte_lie_a_une_autre_organisation(uuid, uuid) FROM PUBLIC;");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION identite.compte_lie_a_une_autre_organisation(uuid, uuid) TO gestionsite_app;");
            migrationBuilder.Sql(
                "CREATE POLICY verification_compte ON identite.membres FOR SELECT TO CURRENT_USER " +
                "USING (current_setting('app.verification_compte', true) = 'on' AND session_user <> current_user);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY verification_compte ON identite.membres;");
            migrationBuilder.Sql("DROP FUNCTION identite.compte_lie_a_une_autre_organisation(uuid, uuid);");
            migrationBuilder.DropTable(
                name: "reinitialisations",
                schema: "identite");
        }
    }
}
