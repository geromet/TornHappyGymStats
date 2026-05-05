using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class NewSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogTypeFetchStates");

            migrationBuilder.DropIndex(
                name: "IX_ImportRuns_StartedAtUtc",
                table: "ImportRuns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AffiliationEvents",
                table: "AffiliationEvents");

            migrationBuilder.DropIndex(
                name: "IX_AffiliationEvents_OccurredAtUtc",
                table: "AffiliationEvents");

            migrationBuilder.DropIndex(
                name: "IX_AffiliationEvents_Scope_AffiliationId_OccurredAtUtc",
                table: "AffiliationEvents");

            migrationBuilder.DropIndex(
                name: "IX_AffiliationEvents_SourceLogId",
                table: "AffiliationEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AffiliationEvent_Scope",
                table: "AffiliationEvents");

            migrationBuilder.DropColumn(
                name: "OccurredAtUtc",
                table: "AffiliationEvents");

            migrationBuilder.DropColumn(
                name: "SourceLogId",
                table: "AffiliationEvents");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "AffiliationEvents",
                newName: "SourceLogEntryId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "AffiliationEvents",
                newName: "PlayerId");

            migrationBuilder.AddColumn<string>(
                name: "NextUrl",
                table: "ImportRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayerId",
                table: "ImportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "AffiliationEvents",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "AffiliationEvents",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AffiliationEvents",
                table: "AffiliationEvents",
                columns: new[] { "PlayerId", "SourceLogEntryId" });

            migrationBuilder.CreateTable(
                name: "LogTypes",
                columns: table => new
                {
                    LogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    LogTypeTitle = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTypes", x => x.LogTypeId);
                });

            migrationBuilder.CreateTable(
                name: "UserLogEntries",
                columns: table => new
                {
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    LogEntryId = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    HappyBeforeApi = table.Column<int>(type: "INTEGER", nullable: true),
                    HappyBeforeTrain = table.Column<int>(type: "INTEGER", nullable: true),
                    HappyBeforeDelta = table.Column<int>(type: "INTEGER", nullable: true),
                    HappyUsed = table.Column<int>(type: "INTEGER", nullable: true),
                    EnergyUsed = table.Column<double>(type: "REAL", nullable: true),
                    StrengthBefore = table.Column<double>(type: "REAL", nullable: true),
                    StrengthIncreased = table.Column<double>(type: "REAL", nullable: true),
                    DefenseBefore = table.Column<double>(type: "REAL", nullable: true),
                    DefenseIncreased = table.Column<double>(type: "REAL", nullable: true),
                    SpeedBefore = table.Column<double>(type: "REAL", nullable: true),
                    SpeedIncreased = table.Column<double>(type: "REAL", nullable: true),
                    DexterityBefore = table.Column<double>(type: "REAL", nullable: true),
                    DexterityIncreased = table.Column<double>(type: "REAL", nullable: true),
                    MaxHappyBefore = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxHappyAfter = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogEntries", x => new { x.PlayerId, x.LogEntryId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_PlayerId_StartedAtUtc",
                table: "ImportRuns",
                columns: new[] { "PlayerId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliationEvents_PlayerId_Scope_AffiliationId",
                table: "AffiliationEvents",
                columns: new[] { "PlayerId", "Scope", "AffiliationId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLogEntries_PlayerId_LogTypeId",
                table: "UserLogEntries",
                columns: new[] { "PlayerId", "LogTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLogEntries_PlayerId_OccurredAtUtc",
                table: "UserLogEntries",
                columns: new[] { "PlayerId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogTypes");

            migrationBuilder.DropTable(
                name: "UserLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_ImportRuns_PlayerId_StartedAtUtc",
                table: "ImportRuns");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AffiliationEvents",
                table: "AffiliationEvents");

            migrationBuilder.DropIndex(
                name: "IX_AffiliationEvents_PlayerId_Scope_AffiliationId",
                table: "AffiliationEvents");

            migrationBuilder.DropColumn(
                name: "NextUrl",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "ImportRuns");

            migrationBuilder.RenameColumn(
                name: "SourceLogEntryId",
                table: "AffiliationEvents",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "AffiliationEvents",
                newName: "Id");

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "AffiliationEvents",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "AffiliationEvents",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAtUtc",
                table: "AffiliationEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SourceLogId",
                table: "AffiliationEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AffiliationEvents",
                table: "AffiliationEvents",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "LogTypeFetchStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FetchScope = table.Column<string>(type: "TEXT", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    LogTypeTitle = table.Column<string>(type: "TEXT", nullable: false),
                    NextUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalFetched = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogTypeFetchStates", x => x.Id);
                    table.CheckConstraint("CK_LogTypeFetchState_FetchScope", "FetchScope IN ('personal', 'faction', 'company')");
                    table.CheckConstraint("CK_LogTypeFetchState_Status", "Status IN ('pending', 'completed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_StartedAtUtc",
                table: "ImportRuns",
                column: "StartedAtUtc");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliationEvent_Scope",
                table: "AffiliationEvents",
                sql: "Scope IN ('faction', 'company')");

            migrationBuilder.CreateIndex(
                name: "IX_LogTypeFetchStates_LogTypeId",
                table: "LogTypeFetchStates",
                column: "LogTypeId",
                unique: true);
        }
    }
}
