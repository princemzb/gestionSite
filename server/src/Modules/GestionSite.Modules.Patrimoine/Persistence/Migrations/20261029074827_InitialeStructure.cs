using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialeStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patrimoine");

            migrationBuilder.CreateTable(
                name: "batiments",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batiments", x => x.id);
                    table.UniqueConstraint("ak_batiments_id_organisation_id", x => new { x.id, x.organisation_id });
                });

            migrationBuilder.CreateTable(
                name: "journal_inventaire",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cible = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    avant = table.Column<string>(type: "jsonb", nullable: true),
                    apres = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_inventaire", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "niveaux",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ordre = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_niveaux", x => x.id);
                    table.UniqueConstraint("ak_niveaux_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.CheckConstraint("ck_niveaux_ordre", "ordre BETWEEN -10 AND 100");
                    table.ForeignKey(
                        name: "fk_niveaux_batiments_batiment_id_organisation_id",
                        columns: x => new { x.batiment_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "batiments",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    niveau_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nom = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    parties_communes = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                    table.UniqueConstraint("ak_zones_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.ForeignKey(
                        name: "fk_zones_batiments_batiment_id_organisation_id",
                        columns: x => new { x.batiment_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "batiments",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zones_niveaux_niveau_id_organisation_id",
                        columns: x => new { x.niveau_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "niveaux",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batiments_nom_par_site",
                schema: "patrimoine",
                table: "batiments",
                columns: new[] { "organisation_id", "site_id", "nom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_inventaire_organisation_id_cible_id_id",
                schema: "patrimoine",
                table: "journal_inventaire",
                columns: new[] { "organisation_id", "cible_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_niveaux_nom_par_batiment",
                schema: "patrimoine",
                table: "niveaux",
                columns: new[] { "batiment_id", "organisation_id", "nom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zones_niveau_id_organisation_id",
                schema: "patrimoine",
                table: "zones",
                columns: new[] { "niveau_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_zones_nom_par_niveau",
                schema: "patrimoine",
                table: "zones",
                columns: new[] { "batiment_id", "organisation_id", "niveau_id", "nom" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_zones_parties_communes_par_batiment",
                schema: "patrimoine",
                table: "zones",
                columns: new[] { "batiment_id", "organisation_id" },
                unique: true,
                filter: "parties_communes");

            foreach (var table in new[] { "batiments", "niveaux", "zones", "journal_inventaire" })
            {
                migrationBuilder.IsolerParOrganisation("patrimoine", table);
            }

            // Rôle de l'API : structure lue, créée et renommée (aucune suppression) ; journal en ajout seul.
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA patrimoine TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT ON patrimoine.__ef_migrations TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON patrimoine.batiments, patrimoine.niveaux, patrimoine.zones TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON patrimoine.journal_inventaire TO gestionsite_app;");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.batiments, patrimoine.niveaux, patrimoine.zones, patrimoine.journal_inventaire FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE SELECT ON patrimoine.__ef_migrations FROM gestionsite_app;");
            migrationBuilder.Sql("REVOKE USAGE ON SCHEMA patrimoine FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "journal_inventaire",
                schema: "patrimoine");

            migrationBuilder.DropTable(
                name: "zones",
                schema: "patrimoine");

            migrationBuilder.DropTable(
                name: "niveaux",
                schema: "patrimoine");

            migrationBuilder.DropTable(
                name: "batiments",
                schema: "patrimoine");
        }
    }
}
