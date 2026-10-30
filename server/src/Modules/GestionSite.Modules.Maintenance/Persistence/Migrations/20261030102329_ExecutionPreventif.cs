using System;
using GestionSite.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Maintenance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionPreventif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "occurrences_preventives",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    echeance = table.Column<DateOnly>(type: "date", nullable: false),
                    statut = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    intervenant_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prestataire = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    referent_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    affectee_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    realisee_le = table.Column<DateOnly>(type: "date", nullable: true),
                    sautee_le = table.Column<DateOnly>(type: "date", nullable: true),
                    motif_saut = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    commentaire_saut = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    controle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrences_preventives", x => x.id);
                    table.UniqueConstraint("ak_occurrences_preventives_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.CheckConstraint("ck_occurrences_preventives_prestataire", "(prestataire IS NULL) = (referent_compte_id IS NULL)");
                    table.CheckConstraint("ck_occurrences_preventives_statut", "(statut = 'Affectee' AND (intervenant_compte_id IS NOT NULL OR prestataire IS NOT NULL)) OR (statut = 'Sautee' AND sautee_le IS NOT NULL AND motif_saut IS NOT NULL) OR (statut IN ('Realisee', 'RealiseeAvecReserves') AND realisee_le IS NOT NULL AND controle_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_occurrences_preventives_plans_plan_id_organisation_id",
                        columns: x => new { x.plan_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "plans_preventifs",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "controles_preventifs",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    echeance = table.Column<DateOnly>(type: "date", nullable: false),
                    commande_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nature = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    corps_etat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    composant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batiment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    realise_le = table.Column<DateOnly>(type: "date", nullable: false),
                    saisi_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    auteur_compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intervenant_compte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prestataire = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    numero_agrement = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    resultat = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    compte_rendu = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    grille_version = table.Column<int>(type: "integer", nullable: true),
                    points = table.Column<string>(type: "jsonb", nullable: false),
                    mesures = table.Column<string>(type: "jsonb", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    libelle_note = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_controles_preventifs", x => x.id);
                    table.UniqueConstraint("ak_controles_preventifs_id_organisation_id", x => new { x.id, x.organisation_id });
                    table.CheckConstraint("ck_controles_preventifs_intervenant", "(intervenant_compte_id IS NULL) <> (prestataire IS NULL)");
                    table.ForeignKey(
                        name: "fk_controles_preventifs_occurrences_preventives_occurrence_id_",
                        columns: x => new { x.occurrence_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "occurrences_preventives",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "photos_controles",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    controle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rang = table.Column<int>(type: "integer", nullable: false),
                    cle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    taille = table.Column<int>(type: "integer", nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos_controles", x => x.id);
                    table.CheckConstraint("ck_photos_controles_rang", "rang BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_photos_controles_controles_preventifs_controle_id_organisat",
                        columns: x => new { x.controle_id, x.organisation_id },
                        principalSchema: "maintenance",
                        principalTable: "controles_preventifs",
                        principalColumns: new[] { "id", "organisation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_controles_preventifs_commande",
                schema: "maintenance",
                table: "controles_preventifs",
                columns: new[] { "organisation_id", "commande_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_controles_preventifs_occurrence_id_organisation_id",
                schema: "maintenance",
                table: "controles_preventifs",
                columns: new[] { "occurrence_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_controles_preventifs_organisation_id_occurrence_id",
                schema: "maintenance",
                table: "controles_preventifs",
                columns: new[] { "organisation_id", "occurrence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_controles_preventifs_organisation_id_site_id_realise_le",
                schema: "maintenance",
                table: "controles_preventifs",
                columns: new[] { "organisation_id", "site_id", "realise_le" });

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_preventives_echeance",
                schema: "maintenance",
                table: "occurrences_preventives",
                columns: new[] { "organisation_id", "plan_id", "echeance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_preventives_organisation_id_site_id_statut",
                schema: "maintenance",
                table: "occurrences_preventives",
                columns: new[] { "organisation_id", "site_id", "statut" });

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_preventives_plan_id_organisation_id",
                schema: "maintenance",
                table: "occurrences_preventives",
                columns: new[] { "plan_id", "organisation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_controles_controle_id_organisation_id_rang",
                schema: "maintenance",
                table: "photos_controles",
                columns: new[] { "controle_id", "organisation_id", "rang" },
                unique: true);

            // Exécution du préventif (US-12, S12-03) : occurrences modifiables ; contrôles et photos en ajout seul ; isolés par organisation.
            foreach (var table in new[] { "occurrences_preventives", "controles_preventifs", "photos_controles" })
            {
                migrationBuilder.IsolerParOrganisation("maintenance", table);
            }

            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON maintenance.occurrences_preventives TO gestionsite_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON maintenance.controles_preventifs, maintenance.photos_controles TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE ALL ON maintenance.occurrences_preventives, maintenance.controles_preventifs, maintenance.photos_controles FROM gestionsite_app;");
            migrationBuilder.DropTable(
                name: "photos_controles",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "controles_preventifs",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "occurrences_preventives",
                schema: "maintenance");
        }
    }
}
