using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderToModelChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "model_changes",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "Unknown");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider",
                table: "model_changes");
        }
    }
}
