using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Identite.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembresEtActivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nom_affiche",
                schema: "identite",
                table: "comptes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "activations",
                schema: "identite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    compte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empreinte = table.Column<byte[]>(type: "bytea", fixedLength: true, maxLength: 32, nullable: false),
                    cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expire_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    utilise_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activations", x => x.id);
                    table.ForeignKey(
                        name: "fk_activations_comptes_compte_id",
                        column: x => x.compte_id,
                        principalSchema: "identite",
                        principalTable: "comptes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activations_compte_id",
                schema: "identite",
                table: "activations",
                column: "compte_id");

            migrationBuilder.CreateIndex(
                name: "ix_activations_empreinte",
                schema: "identite",
                table: "activations",
                column: "empreinte",
                unique: true);

            // Liens d'activation : données hors organisation (comme les comptes), lues par empreinte seulement.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON identite.activations TO gestionsite_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activations",
                schema: "identite");

            migrationBuilder.DropColumn(
                name: "nom_affiche",
                schema: "identite",
                table: "comptes");
        }
    }
}
