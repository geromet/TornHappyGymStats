using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906130000_AddWarReadinessDeclarations")]
public partial class AddWarReadinessDeclarations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarReadinessDeclarations",
            columns: table => new
            {
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                MemberId = table.Column<long>(type: "bigint", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_WarReadinessDeclarations",
                    x => new { x.FactionId, x.WarId, x.MemberId });
                table.CheckConstraint(
                    "CK_WarReadinessDeclarations_State",
                    "\"State\" IN (1, 2, 3)");
                table.CheckConstraint(
                    "CK_WarReadinessDeclarations_Window",
                    "\"WindowEndUtc\" > \"WindowStartUtc\"");
                table.CheckConstraint(
                    "CK_WarReadinessDeclarations_UpdatedWithinWindow",
                    "\"UpdatedAtUtc\" <= \"WindowEndUtc\"");
                table.CheckConstraint(
                    "CK_WarReadinessDeclarations_Revision",
                    "\"Revision\" > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarReadinessDeclarations_FactionId_WarId",
            table: "WarReadinessDeclarations",
            columns: new[] { "FactionId", "WarId" });

        migrationBuilder.CreateIndex(
            name: "IX_WarReadinessDeclarations_WarId_WindowEndUtc",
            table: "WarReadinessDeclarations",
            columns: new[] { "WarId", "WindowEndUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarReadinessDeclarations");
    }
}
