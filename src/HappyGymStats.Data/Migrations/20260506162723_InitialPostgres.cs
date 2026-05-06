using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffiliationEvents",
                columns: table => new
                {
                    AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLogEntryId = table.Column<string>(type: "text", nullable: false),
                    LogTypeId = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    AffiliationId = table.Column<int>(type: "integer", nullable: false),
                    SenderId = table.Column<int>(type: "integer", nullable: true),
                    PositionBefore = table.Column<int>(type: "integer", nullable: true),
                    PositionAfter = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliationEvents", x => new { x.AnonymousId, x.SourceLogEntryId });
                });

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnonymousId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    PagesFetched = table.Column<int>(type: "integer", nullable: false),
                    LogsFetched = table.Column<long>(type: "bigint", nullable: false),
                    LogsAppended = table.Column<long>(type: "bigint", nullable: false),
                    NextUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogTypes",
                columns: table => new
                {
                    LogTypeId = table.Column<int>(type: "integer", nullable: false),
                    LogTypeTitle = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTypes", x => x.LogTypeId);
                });

            migrationBuilder.CreateTable(
                name: "ModifierProvenance",
                columns: table => new
                {
                    AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogEntryId = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    FactionId = table.Column<int>(type: "integer", nullable: true),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModifierProvenance", x => new { x.AnonymousId, x.LogEntryId, x.Scope });
                });

            migrationBuilder.CreateTable(
                name: "UserLogEntries",
                columns: table => new
                {
                    AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogEntryId = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LogTypeId = table.Column<int>(type: "integer", nullable: false),
                    HappyBeforeApi = table.Column<int>(type: "integer", nullable: true),
                    HappyBeforeTrain = table.Column<int>(type: "integer", nullable: true),
                    HappyBeforeDelta = table.Column<int>(type: "integer", nullable: true),
                    HappyUsed = table.Column<int>(type: "integer", nullable: true),
                    HappyIncreased = table.Column<int>(type: "integer", nullable: true),
                    HappyDecreased = table.Column<int>(type: "integer", nullable: true),
                    EnergyUsed = table.Column<double>(type: "double precision", nullable: true),
                    StrengthBefore = table.Column<double>(type: "double precision", nullable: true),
                    StrengthIncreased = table.Column<double>(type: "double precision", nullable: true),
                    DefenseBefore = table.Column<double>(type: "double precision", nullable: true),
                    DefenseIncreased = table.Column<double>(type: "double precision", nullable: true),
                    SpeedBefore = table.Column<double>(type: "double precision", nullable: true),
                    SpeedIncreased = table.Column<double>(type: "double precision", nullable: true),
                    DexterityBefore = table.Column<double>(type: "double precision", nullable: true),
                    DexterityIncreased = table.Column<double>(type: "double precision", nullable: true),
                    MaxHappyBefore = table.Column<int>(type: "integer", nullable: true),
                    MaxHappyAfter = table.Column<int>(type: "integer", nullable: true),
                    PropertyId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogEntries", x => new { x.AnonymousId, x.LogEntryId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliationEvents_AnonymousId_Scope_AffiliationId",
                table: "AffiliationEvents",
                columns: new[] { "AnonymousId", "Scope", "AffiliationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_AnonymousId_StartedAtUtc",
                table: "ImportRuns",
                columns: new[] { "AnonymousId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_Outcome",
                table: "ImportRuns",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_ModifierProvenance_AnonymousId_VerificationStatus",
                table: "ModifierProvenance",
                columns: new[] { "AnonymousId", "VerificationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLogEntries_AnonymousId_LogTypeId",
                table: "UserLogEntries",
                columns: new[] { "AnonymousId", "LogTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLogEntries_AnonymousId_OccurredAtUtc",
                table: "UserLogEntries",
                columns: new[] { "AnonymousId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliationEvents");

            migrationBuilder.DropTable(
                name: "ImportRuns");

            migrationBuilder.DropTable(
                name: "LogTypes");

            migrationBuilder.DropTable(
                name: "ModifierProvenance");

            migrationBuilder.DropTable(
                name: "UserLogEntries");
        }
    }
}
