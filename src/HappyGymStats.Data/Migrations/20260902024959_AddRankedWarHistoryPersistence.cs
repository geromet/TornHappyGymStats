using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankedWarHistoryPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RankedWarHistory",
                columns: table => new
                {
                    WarId = table.Column<long>(type: "bigint", nullable: false),
                    FactionId = table.Column<long>(type: "bigint", nullable: false),
                    FactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OpponentFactionId = table.Column<long>(type: "bigint", nullable: false),
                    OpponentFactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WinnerFactionId = table.Column<long>(type: "bigint", nullable: true),
                    FactionScore = table.Column<int>(type: "integer", nullable: true),
                    FactionChain = table.Column<int>(type: "integer", nullable: true),
                    OpponentScore = table.Column<int>(type: "integer", nullable: true),
                    OpponentChain = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IngestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportCapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReportIngestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedWarHistory", x => x.WarId);
                });

            migrationBuilder.CreateTable(
                name: "RankedWarReportMembers",
                columns: table => new
                {
                    WarId = table.Column<long>(type: "bigint", nullable: false),
                    FactionId = table.Column<long>(type: "bigint", nullable: false),
                    MemberId = table.Column<long>(type: "bigint", nullable: false),
                    FactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MemberName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Chain = table.Column<int>(type: "integer", nullable: false),
                    Attacks = table.Column<int>(type: "integer", nullable: false),
                    StatusState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StatusUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsIdleAttacker = table.Column<bool>(type: "boolean", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IngestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedWarReportMembers", x => new { x.WarId, x.FactionId, x.MemberId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarHistory_EndedAtUtc",
                table: "RankedWarHistory",
                column: "EndedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarHistory_FactionId_StartedAtUtc",
                table: "RankedWarHistory",
                columns: new[] { "FactionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarHistory_OpponentFactionId_StartedAtUtc",
                table: "RankedWarHistory",
                columns: new[] { "OpponentFactionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarReportMembers_FactionId_MemberId",
                table: "RankedWarReportMembers",
                columns: new[] { "FactionId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarReportMembers_MemberId",
                table: "RankedWarReportMembers",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedWarReportMembers_WarId_FactionId",
                table: "RankedWarReportMembers",
                columns: new[] { "WarId", "FactionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankedWarHistory");

            migrationBuilder.DropTable(
                name: "RankedWarReportMembers");
        }
    }
}
