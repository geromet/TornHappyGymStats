using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedWarHistoryBackfillState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankedWarHistoryBackfillState",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NextHistoryPageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastProcessedWarId = table.Column<long>(type: "bigint", nullable: true),
                    PagesProcessed = table.Column<long>(type: "bigint", nullable: false),
                    ReportsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastFailureCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedWarHistoryBackfillState", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarHistoryBackfillState_NextRetryAtUtc",
                table: "RankedWarHistoryBackfillState",
                column: "NextRetryAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankedWarHistoryBackfillState");
        }
    }
}
