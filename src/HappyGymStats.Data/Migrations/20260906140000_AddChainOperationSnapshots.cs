using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906140000_AddChainOperationSnapshots")]
public partial class AddChainOperationSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChainOperationSnapshots",
            columns: table => new
            {
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_ChainOperationSnapshots",
                    x => new { x.FactionId, x.WarId });
                table.CheckConstraint(
                    "CK_ChainOperationSnapshots_FactionId",
                    "\"FactionId\" > 0");
                table.CheckConstraint(
                    "CK_ChainOperationSnapshots_WarId",
                    "\"WarId\" > 0");
                table.CheckConstraint(
                    "CK_ChainOperationSnapshots_Revision",
                    "\"Revision\" > 0");
                table.CheckConstraint(
                    "CK_ChainOperationSnapshots_PayloadObject",
                    "jsonb_typeof(\"PayloadJson\") = 'object'");
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChainOperationSnapshots_WarId",
            table: "ChainOperationSnapshots",
            column: "WarId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChainOperationSnapshots");
    }
}
