using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_aliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    provider_id = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    canonical_id = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_aliases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_aliases_provider_id",
                table: "model_aliases",
                column: "provider_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_aliases");
        }
    }
}
