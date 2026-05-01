using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalFieldsToSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "model_snapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "metrics_json",
                table: "model_snapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metadata_json",
                table: "model_snapshots");

            migrationBuilder.DropColumn(
                name: "metrics_json",
                table: "model_snapshots");
        }
    }
}
