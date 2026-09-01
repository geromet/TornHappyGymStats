using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWarPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarCurrent",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WarId = table.Column<long>(type: "bigint", nullable: true),
                    FactionId = table.Column<long>(type: "bigint", nullable: true),
                    FactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OpponentFactionId = table.Column<long>(type: "bigint", nullable: true),
                    OpponentFactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarCurrent", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "WarPollerHeartbeats",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PollStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PollCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ActiveWarId = table.Column<long>(type: "bigint", nullable: true),
                    StaleAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PollIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    FailureBackoffSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarPollerHeartbeats", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "WarRosterSnapshots",
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
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarRosterSnapshots", x => new { x.WarId, x.FactionId, x.MemberId });
                });

            migrationBuilder.CreateTable(
                name: "WarScoreSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarId = table.Column<long>(type: "bigint", nullable: false),
                    FactionId = table.Column<long>(type: "bigint", nullable: false),
                    FactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FactionScore = table.Column<int>(type: "integer", nullable: false),
                    FactionChain = table.Column<int>(type: "integer", nullable: false),
                    OpponentFactionId = table.Column<long>(type: "bigint", nullable: false),
                    OpponentFactionName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OpponentScore = table.Column<int>(type: "integer", nullable: false),
                    OpponentChain = table.Column<int>(type: "integer", nullable: false),
                    SampledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarScoreSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarCurrent_WarId",
                table: "WarCurrent",
                column: "WarId",
                unique: true,
                filter: "\"WarId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WarPollerHeartbeats_ActiveWarId",
                table: "WarPollerHeartbeats",
                column: "ActiveWarId");

            migrationBuilder.CreateIndex(
                name: "IX_WarPollerHeartbeats_Phase_UpdatedAtUtc",
                table: "WarPollerHeartbeats",
                columns: new[] { "Phase", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarRosterSnapshots_WarId_CapturedAtUtc",
                table: "WarRosterSnapshots",
                columns: new[] { "WarId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WarScoreSamples_WarId_SampledAtUtc",
                table: "WarScoreSamples",
                columns: new[] { "WarId", "SampledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WarCurrent");

            migrationBuilder.DropTable(
                name: "WarPollerHeartbeats");

            migrationBuilder.DropTable(
                name: "WarRosterSnapshots");

            migrationBuilder.DropTable(
                name: "WarScoreSamples");
        }
    }
}
