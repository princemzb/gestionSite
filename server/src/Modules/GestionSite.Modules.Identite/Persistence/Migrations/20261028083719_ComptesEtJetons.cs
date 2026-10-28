using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComptesEtJetons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comptes",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comptes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comptes_connexions_externes",
                schema: "identite",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comptes_connexions_externes", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_comptes_connexions_externes_comptes_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comptes_jetons",
                schema: "identite",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comptes_jetons", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_comptes_jetons_comptes_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comptes_revendications",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comptes_revendications", x => x.id);
                    table.ForeignKey(
                        name: "fk_comptes_revendications_comptes_user_id",
                        column: x => x.user_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jetons_renouvellement",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    famille = table.Column<Guid>(type: "uuid", nullable: false),
                    empreinte = table.Column<byte[]>(type: "bytea", fixedLength: true, maxLength: 32, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expire_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoque_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remplace_par = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jetons_renouvellement", x => x.id);
                    table.ForeignKey(
                        name: "fk_jetons_renouvellement_comptes_compte_id",
                        column: x => x.compte_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_membres_compte_id_actives",
                schema: "identite",
                table: "membres",
                column: "compte_id",
                filter: "revoque_le IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_comptes_normalized_email",
                schema: "identite",
                table: "comptes",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comptes_normalized_user_name",
                schema: "identite",
                table: "comptes",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comptes_connexions_externes_user_id",
                schema: "identite",
                table: "comptes_connexions_externes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_comptes_revendications_user_id",
                schema: "identite",
                table: "comptes_revendications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jetons_renouvellement_compte_id",
                schema: "identite",
                table: "jetons_renouvellement",
                column: "compte_id");

            migrationBuilder.CreateIndex(
                name: "ix_jetons_renouvellement_empreinte",
                schema: "identite",
                table: "jetons_renouvellement",
                column: "empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jetons_renouvellement_famille",
                schema: "identite",
                table: "jetons_renouvellement",
                column: "famille");

            migrationBuilder.AddForeignKey(
                name: "fk_membres_comptes_compte_id",
                schema: "identite",
                table: "membres",
                column: "compte_id",
                principalSchema: "identite",
                principalTable: "comptes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            AccorderRoleApplicatif(migrationBuilder);
            AutoriserLectureParCompte(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "organisations", "sites", "membres", "roles_attribues" })
            {
                migrationBuilder.Sql($"DROP POLICY lecture_par_compte ON identite.{table};");
            }

            // Tables supprimées ci-dessous : leurs droits disparaissent avec elles.
            migrationBuilder.DropForeignKey(
                name: "fk_membres_comptes_compte_id",
                schema: "identite",
                table: "membres");

            migrationBuilder.DropTable(
                name: "comptes_connexions_externes",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "comptes_jetons",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "comptes_revendications",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "jetons_renouvellement",
                schema: "identite");

            migrationBuilder.DropTable(
                name: "comptes",
                schema: "identite");

            migrationBuilder.DropIndex(
                name: "ix_membres_compte_id_actives",
                schema: "identite",
                table: "membres");
        }

        // Nom figé (CompteCourant.VariableSession) : une migration appliquée ne doit pas changer.
        private const string CompteDeSession = "NULLIF(current_setting('app.compte_id', true), '')::uuid";

        private const string AdhesionActiveDuCompte =
            "SELECT 1 FROM identite.membres m WHERE m.compte_id = " + CompteDeSession + " AND m.revoque_le IS NULL";

        // Comptes et jetons : données hors organisation, sans RLS (connexion avant tout contexte) ;
        // seule l'API les lit, par e-mail ou empreinte, jamais exposés en liste.
        private static void AccorderRoleApplicatif(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON identite.comptes, identite.comptes_revendications, " +
                "identite.comptes_connexions_externes, identite.comptes_jetons, identite.jetons_renouvellement TO gestionsite_app;");

        // GET /me : un compte lit ses propres adhésions actives dans toutes ses organisations, sans
        // organisation courante. Politiques permissives en lecture seule, ajoutées (OU) à l'isolation
        // par organisation ; aucune écriture n'est ouverte par compte.
        private static void AutoriserLectureParCompte(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.membres FOR SELECT " +
                $"USING (compte_id = {CompteDeSession} AND revoque_le IS NULL);");
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.roles_attribues FOR SELECT " +
                $"USING (EXISTS ({AdhesionActiveDuCompte} AND m.id = roles_attribues.membre_id));");
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.organisations FOR SELECT " +
                $"USING (EXISTS ({AdhesionActiveDuCompte} AND m.organisation_id = organisations.id));");
            // Sites autorisés seulement : tous pour l'administrateur, sinon ceux où le compte a un rôle.
            migrationBuilder.Sql(
                "CREATE POLICY lecture_par_compte ON identite.sites FOR SELECT USING (EXISTS (" +
                "SELECT 1 FROM identite.membres m JOIN identite.roles_attribues r ON r.membre_id = m.id " +
                $"WHERE m.compte_id = {CompteDeSession} AND m.revoque_le IS NULL AND r.organisation_id = sites.organisation_id " +
                "AND (r.role = 'Administrateur' OR r.site_id = sites.id)));");
        }
    }
}
