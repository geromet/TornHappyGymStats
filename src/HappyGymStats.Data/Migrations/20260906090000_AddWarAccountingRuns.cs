using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906090000_AddWarAccountingRuns")]
public partial class AddWarAccountingRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarAccountingRuns",
            columns: table => new
            {
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                ObjectiveVersion = table.Column<int>(type: "integer", nullable: false),
                FrozenBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                FrozenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarAccountingRuns", x => x.RunId);
                table.CheckConstraint("CK_WarAccountingRuns_FactionId", "\"FactionId\" > 0");
                table.CheckConstraint("CK_WarAccountingRuns_WarId", "\"WarId\" > 0");
                table.CheckConstraint("CK_WarAccountingRuns_ObjectiveVersion", "\"ObjectiveVersion\" > 0");
                table.CheckConstraint("CK_WarAccountingRuns_FrozenBy", "length(btrim(\"FrozenBy\")) > 0");
                table.ForeignKey(
                    name: "FK_WarAccountingRuns_WarObjectiveVersions_FactionId_WarId_ObjectiveVersion",
                    columns: x => new { x.FactionId, x.WarId, x.ObjectiveVersion },
                    principalTable: "WarObjectiveVersions",
                    principalColumns: new[] { "FactionId", "WarId", "Version" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingRuns_FactionId_WarId_ObjectiveVersion",
            table: "WarAccountingRuns",
            columns: new[] { "FactionId", "WarId", "ObjectiveVersion" });

        migrationBuilder.Sql("""
            CREATE FUNCTION prevent_war_accounting_run_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'WarAccountingRuns is immutable after freeze';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarAccountingRuns_Immutable"
            BEFORE UPDATE OR DELETE ON "WarAccountingRuns"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_accounting_run_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarAccountingRuns");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_war_accounting_run_mutation();");
    }
}
