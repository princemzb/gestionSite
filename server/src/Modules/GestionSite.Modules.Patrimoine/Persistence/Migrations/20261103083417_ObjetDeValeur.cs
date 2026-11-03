using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionSite.Modules.Patrimoine.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObjetDeValeur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "objet_de_valeur",
                schema: "patrimoine",
                table: "composants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "objet_de_valeur",
                schema: "patrimoine",
                table: "composants");
        }
    }
}
