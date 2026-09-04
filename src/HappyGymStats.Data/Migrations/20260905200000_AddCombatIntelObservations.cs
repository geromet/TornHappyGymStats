using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260905200000_AddCombatIntelObservations")]
public partial class AddCombatIntelObservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CombatIntelObservations",
            columns: table => new
            {
                ObservationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PlayerId = table.Column<long>(type: "bigint", nullable: false),
                Provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Classification = table.Column<int>(type: "integer", nullable: false),
                Value = table.Column<decimal>(type: "numeric(29,6)", precision: 29, scale: 6, nullable: true),
                LowerBound = table.Column<decimal>(type: "numeric(29,6)", precision: 29, scale: 6, nullable: true),
                UpperBound = table.Column<decimal>(type: "numeric(29,6)", precision: 29, scale: 6, nullable: true),
                ProviderMetadata = table.Column<string>(type: "text", nullable: true),
                VisibilityScope = table.Column<int>(type: "integer", nullable: false),
                VisibilityOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SupersedesObservationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CombatIntelObservations", x => x.ObservationId);
                table.ForeignKey(
                    name: "FK_CombatIntelObservations_CombatIntelObservations_SupersedesObservationId",
                    column: x => x.SupersedesObservationId,
                    principalTable: "CombatIntelObservations",
                    principalColumn: "ObservationId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CombatIntelObservations_PlayerId_ObservedAtUtc",
            table: "CombatIntelObservations",
            columns: new[] { "PlayerId", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CombatIntelObservations_Provider_ObservedAtUtc",
            table: "CombatIntelObservations",
            columns: new[] { "Provider", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CombatIntelObservations_PlayerId_Provider_ObservedAtUtc",
            table: "CombatIntelObservations",
            columns: new[] { "PlayerId", "Provider", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CombatIntelObservations_VisibilityScope_VisibilityOwner_ObservedAtUtc",
            table: "CombatIntelObservations",
            columns: new[] { "VisibilityScope", "VisibilityOwner", "ObservedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_CombatIntelObservations_SupersedesObservationId",
            table: "CombatIntelObservations",
            column: "SupersedesObservationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CombatIntelObservations");
    }
}
