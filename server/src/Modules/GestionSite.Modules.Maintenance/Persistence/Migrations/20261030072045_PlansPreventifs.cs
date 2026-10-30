using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlansPreventifs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans_preventifs",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposition_cle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cible = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    batiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    composant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corps_etat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    nature = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    libelle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    @base = table.Column<string>(name: "base", type: "character varying(16)", maxLength: 16, nullable: false),
                    reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    type_intervenant = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    intervenant_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prestataire = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    referent_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donneur_ordre = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    periode_valeur = table.Column<int>(type: "integer", nullable: true),
                    periode_unite = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    premiere_echeance = table.Column<DateOnly>(type: "date", nullable: false),
                    suspendu = table.Column<bool>(type: "boolean", nullable: false),
                    motif_suspension = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cree_par_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans_preventifs", x => x.id);
                    table.UniqueConstraint("ak_plans_preventifs_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.CheckConstraint("ck_plans_preventifs_fixe", "mode = 'Glissant' OR periode_valeur IS NOT NULL");
                    table.CheckConstraint("ck_plans_preventifs_periode", "(periode_valeur IS NULL) = (periode_unite IS NULL) AND (periode_valeur IS NULL OR periode_valeur BETWEEN 1 AND 120)");
                    table.CheckConstraint("ck_plans_preventifs_suspension", "suspendu = (motif_suspension IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "propositions_ecartees",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    motif = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    commentaire = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ecartee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_propositions_ecartees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_preventif",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_preventif", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_preventif_plans_preventifs_plan_id_organisation_id",
                        columns: x => new { x.plan_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "plans_preventifs",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_preventif_organisation_id_plan_id_id",
                schema: "maintenance",
                table: "journal_preventif",
                columns: new[] { "organisation_id", "plan_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_preventif_plan_id_organisation_id",
                schema: "maintenance",
                table: "journal_preventif",
                columns: new[] { "plan_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_preventifs_commande",
                schema: "maintenance",
                table: "plans_preventifs",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_preventifs_organisation_id_site_id_composant_id",
                schema: "maintenance",
                table: "plans_preventifs",
                columns: new[] { "organisation_id", "site_id", "composant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_preventifs_proposition",
                schema: "maintenance",
                table: "plans_preventifs",
                columns: new[] { "organisation_id", "site_id", "proposition_cle" },
                unique: true,
                filter: "proposition_cle IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_propositions_ecartees_organisation_id_site_id_cle",
                schema: "maintenance",
                table: "propositions_ecartees",
                columns: new[] { "organisation_id", "site_id", "cle" },
                unique: true);

            // Préventif (US-12, S12-02) : plans modifiables ; propositions écartées et journal en ajout seul ; isolés par organisation.
            foreach (var table in new[] { "plans_preventifs", "propositions_ecartees", "journal_preventif" })
            {
                migrationBuilder.IsolerParOrganisation("maintenance", table);
            }

            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON maintenance.plans_preventifs TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.propositions_ecartees, maintenance.journal_preventif TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.plans_preventifs, maintenance.propositions_ecartees, maintenance.journal_preventif FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "journal_preventif",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "propositions_ecartees",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "plans_preventifs",
                schema: "maintenance");
        }
    }
}
