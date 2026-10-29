using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Composants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "composants",
                schema: "patrimoine",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nom = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    corps_etat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    emplacement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    marque = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    modele = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    numero_serie = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    annee_mise_en_service = table.Column<int>(type: "integer", nullable: true),
                    mois_mise_en_service = table.Column<int>(type: "integer", nullable: true),
                    remarques = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    soumis_a_verification = table.Column<bool>(type: "boolean", nullable: false),
                    unite = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    quantite = table.Column<int>(type: "integer", nullable: true),
                    quantite_bon = table.Column<int>(type: "integer", nullable: true),
                    quantite_satisfaisant = table.Column<int>(type: "integer", nullable: true),
                    quantite_degrade = table.Column<int>(type: "integer", nullable: true),
                    quantite_mauvais = table.Column<int>(type: "integer", nullable: true),
                    quantite_critique = table.Column<int>(type: "integer", nullable: true),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retire_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motif_retrait = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_composants", x => x.id);
                    table.CheckConstraint("ck_composants_lot", "(unite IS NULL) = (quantite IS NULL) AND (quantite IS NULL OR quantite BETWEEN 1 AND 100000)");
                    table.CheckConstraint("ck_composants_mise_en_service", "(mois_mise_en_service IS NULL OR (annee_mise_en_service IS NOT NULL AND mois_mise_en_service BETWEEN 1 AND 12))");
                    table.CheckConstraint("ck_composants_repartition", "(quantite_bon IS NULL AND quantite_satisfaisant IS NULL AND quantite_degrade IS NULL AND quantite_mauvais IS NULL AND quantite_critique IS NULL) OR (quantite IS NOT NULL AND quantite_bon >= 0 AND quantite_satisfaisant >= 0 AND quantite_degrade >= 0 AND quantite_mauvais >= 0 AND quantite_critique >= 0 AND quantite_bon + quantite_satisfaisant + quantite_degrade + quantite_mauvais + quantite_critique = quantite)");
                    table.CheckConstraint("ck_composants_retrait", "(retire_le IS NULL) = (motif_retrait IS NULL)");
                    table.ForeignKey(
                        name: "fk_composants_zones_zone_id_organisation_id",
                        columns: x => new { x.zone_id, x.organisation_id },
                        principalSchema: "patrimoine",
                        principalTable: "zones",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_composants_organisation_id_site_id_zone_id",
                schema: "patrimoine",
                table: "composants",
                columns: new[] { "organisation_id", "site_id", "zone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_composants_zone_id_organisation_id",
                schema: "patrimoine",
                table: "composants",
                columns: new[] { "zone_id", "organisation_id" });

            // Isolation des organisations (ADR-10), comme les tables de la structure : retrait sans suppression, donc sans DELETE.
            migrationBuilder.IsolerParOrganisation("patrimoine", "composants");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON patrimoine.composants TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON patrimoine.composants FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "composants",
                schema: "patrimoine");
        }
    }
}
