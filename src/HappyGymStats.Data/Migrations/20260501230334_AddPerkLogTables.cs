using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerkLogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffiliationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceLogId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    AffiliationId = table.Column<long>(type: "INTEGER", nullable: false),
                    SenderId = table.Column<long>(type: "INTEGER", nullable: true),
                    PositionBefore = table.Column<long>(type: "INTEGER", nullable: true),
                    PositionAfter = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliationEvents", x => x.Id);
                    table.CheckConstraint("CK_AffiliationEvent_Scope", "Scope IN ('faction', 'company')");
                });

            migrationBuilder.CreateTable(
                name: "LogTypeFetchStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    LogTypeTitle = table.Column<string>(type: "TEXT", nullable: false),
                    FetchScope = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    NextUrl = table.Column<string>(type: "TEXT", nullable: true),
                    TotalFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTypeFetchStates", x => x.Id);
                    table.CheckConstraint("CK_LogTypeFetchState_FetchScope", "FetchScope IN ('personal', 'faction', 'company')");
                    table.CheckConstraint("CK_LogTypeFetchState_Status", "Status IN ('pending', 'completed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliationEvents_OccurredAtUtc",
                table: "AffiliationEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliationEvents_Scope_AffiliationId_OccurredAtUtc",
                table: "AffiliationEvents",
                columns: new[] { "Scope", "AffiliationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliationEvents_SourceLogId",
                table: "AffiliationEvents",
                column: "SourceLogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogTypeFetchStates_LogTypeId",
                table: "LogTypeFetchStates",
                column: "LogTypeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliationEvents");

            migrationBuilder.DropTable(
                name: "LogTypeFetchStates");
        }
    }
}
