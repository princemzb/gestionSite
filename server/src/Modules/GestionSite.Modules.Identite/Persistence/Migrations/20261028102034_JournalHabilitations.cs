using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalHabilitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_habilitations",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membre_id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motif = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    roles_avant = table.Column<string>(type: "jsonb", nullable: true),
                    roles_apres = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_habilitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_habilitations_membres_membre_id_organisation_id",
                        columns: x => new { x.membre_id, x.organisation_id },
                        principalSchema: "identite",
                        principalTable: "membres",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_habilitations_membre_id_organisation_id",
                schema: "identite",
                table: "journal_habilitations",
                columns: new[] { "membre_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_habilitations_organisation_id_le",
                schema: "identite",
                table: "journal_habilitations",
                columns: new[] { "organisation_id", "le" });

            // Isolation par organisation (RLS activée et forcée), comme toute table d'organisation.
            migrationBuilder.Sql("ALTER TABLE identite.journal_habilitations ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE identite.journal_habilitations FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY isolation_organisation ON identite.journal_habilitations " +
                "USING (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid) " +
                "WITH CHECK (organisation_id = NULLIF(current_setting('app.organisation_id', true), '')::uuid);");

            // Journal en ajout seulement (RG-31) : ni modification ni suppression par l'API.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON identite.journal_habilitations TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_habilitations",
                schema: "identite");
        }
    }
}
