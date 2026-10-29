using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SitesPublics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "numero_urgence",
                schema: "identite",
                table: "sites",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "codes_sites_publics",
                schema: "identite",
                columns: table => new
                {
                    code = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codes_sites_publics", x => x.code);
                    table.ForeignKey(
                        name: "fk_codes_sites_publics_sites_site_id_organisation_id",
                        columns: x => new { x.site_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codes_sites_publics_site_id_organisation_id",
                schema: "identite",
                table: "codes_sites_publics",
                columns: new[] { "site_id", "organisation_id" },
                unique: true);

            // Lecture par code sans organisation (dépôt public : le code est la clé) ; écriture seulement par
            // l'organisation du site. Pas de modification ni de suppression par l'API (cascade avec le site).
            migrationBuilder.Sql("ALTER TABLE identite.codes_sites_publics ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE identite.codes_sites_publics FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("CREATE POLICY lecture_par_code ON identite.codes_sites_publics FOR SELECT USING (true);");
            migrationBuilder.Sql(
                "CREATE POLICY ecriture_organisation ON identite.codes_sites_publics FOR INSERT " +
                "WITH CHECK (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON identite.codes_sites_publics TO gestionsite_app;");

            // Sites existants : un code chacun. Le propriétaire est soumis à la RLS (FORCE) : levée le temps de
            // cette insertion seulement, dans la transaction de la migration. Octets tirés de gen_random_uuid
            // (aléa fort), hors octets de version et de variante ; même alphabet que CodeSitePublic.
            migrationBuilder.Sql("ALTER TABLE identite.sites NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE identite.codes_sites_publics NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "INSERT INTO identite.codes_sites_publics (code, site_id, organisation_id, cree_le) " +
                "SELECT (SELECT string_agg(substr('23456789ABCDEFGHJKMNPQRSTVWXYZ', 1 + get_byte(t.octets, i) % 30, 1), '' ORDER BY i) " +
                "FROM unnest(ARRAY[0, 1, 2, 3, 4, 5, 16, 17, 18, 19]) AS i), t.id, t.organisation_id, now() " +
                "FROM (SELECT s.id, s.organisation_id, uuid_send(gen_random_uuid()) || uuid_send(gen_random_uuid()) AS octets " +
                "FROM identite.sites s) t;");
            migrationBuilder.Sql("ALTER TABLE identite.codes_sites_publics FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE identite.sites FORCE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON identite.codes_sites_publics FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "codes_sites_publics",
                schema: "identite");

            migrationBuilder.DropColumn(
                name: "numero_urgence",
                schema: "identite",
                table: "sites");
        }
    }
}
