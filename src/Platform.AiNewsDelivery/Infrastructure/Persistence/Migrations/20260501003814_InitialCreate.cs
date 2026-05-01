using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.AiNewsDelivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    model_id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    change_type = table.Column<string>(type: "TEXT", nullable: false),
                    field_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    old_value = table.Column<string>(type: "TEXT", nullable: true),
                    new_value = table.Column<string>(type: "TEXT", nullable: true),
                    delta_percent = table.Column<double>(type: "REAL", nullable: true),
                    severity = table.Column<string>(type: "TEXT", nullable: false),
                    detected_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    dispatched = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    dispatched_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    model_id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    collected_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    data_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    raw_data = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "worker_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    finished_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    models_collected = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    changes_detected = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    errors_log = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_changes_detected_dispatched",
                table: "model_changes",
                columns: new[] { "detected_at", "dispatched" });

            migrationBuilder.CreateIndex(
                name: "ix_changes_model_type",
                table: "model_changes",
                columns: new[] { "model_id", "change_type" });

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_model_collected_desc",
                table: "model_snapshots",
                columns: new[] { "model_id", "collected_at" });

            migrationBuilder.CreateIndex(
                name: "ux_snapshots_model_provider_collected",
                table: "model_snapshots",
                columns: new[] { "model_id", "provider", "collected_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_changes");

            migrationBuilder.DropTable(
                name: "model_snapshots");

            migrationBuilder.DropTable(
                name: "worker_runs");
        }
    }
}
