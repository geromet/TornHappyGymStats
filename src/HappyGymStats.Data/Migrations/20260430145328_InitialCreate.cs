using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DerivedGymTrains",
                columns: table => new
                {
                    LogId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    HappyBeforeTrain = table.Column<int>(type: "INTEGER", nullable: false),
                    HappyAfterTrain = table.Column<int>(type: "INTEGER", nullable: false),
                    HappyUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    RegenTicksApplied = table.Column<long>(type: "INTEGER", nullable: false),
                    RegenHappyGained = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxHappyAtTimeUtc = table.Column<int>(type: "INTEGER", nullable: true),
                    ClampedToMax = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivedGymTrains", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "DerivedHappyEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SourceLogId = table.Column<string>(type: "TEXT", nullable: true),
                    HappyBeforeEvent = table.Column<int>(type: "INTEGER", nullable: true),
                    HappyAfterEvent = table.Column<int>(type: "INTEGER", nullable: true),
                    Delta = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivedHappyEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "ImportCheckpoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NextUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LastLogId = table.Column<string>(type: "TEXT", nullable: true),
                    LastLogTimestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastLogTitle = table.Column<string>(type: "TEXT", nullable: true),
                    LastLogCategory = table.Column<string>(type: "TEXT", nullable: true),
                    TotalFetchedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalAppendedCount = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRunStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastRunCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastRunOutcome = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    PagesFetched = table.Column<int>(type: "INTEGER", nullable: false),
                    LogsFetched = table.Column<long>(type: "INTEGER", nullable: false),
                    LogsAppended = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawUserLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    RawJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawUserLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DerivedGymTrains_OccurredAtUtc",
                table: "DerivedGymTrains",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DerivedHappyEvents_EventType",
                table: "DerivedHappyEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_DerivedHappyEvents_OccurredAtUtc",
                table: "DerivedHappyEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DerivedHappyEvents_SourceLogId",
                table: "DerivedHappyEvents",
                column: "SourceLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportCheckpoints_Name",
                table: "ImportCheckpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_Outcome",
                table: "ImportRuns",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_StartedAtUtc",
                table: "ImportRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RawUserLogs_LogId",
                table: "RawUserLogs",
                column: "LogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawUserLogs_OccurredAtUtc",
                table: "RawUserLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DerivedGymTrains");

            migrationBuilder.DropTable(
                name: "DerivedHappyEvents");

            migrationBuilder.DropTable(
                name: "ImportCheckpoints");

            migrationBuilder.DropTable(
                name: "ImportRuns");

            migrationBuilder.DropTable(
                name: "RawUserLogs");
        }
    }
}
