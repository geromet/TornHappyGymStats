using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260905020800_AddWarObjectiveVersions")]
public partial class AddWarObjectiveVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarObjectiveVersions",
            columns: table => new
            {
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                Mode = table.Column<int>(type: "integer", nullable: false),
                IsExplicit = table.Column<bool>(type: "boolean", nullable: false),
                StopAtFactionScore = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarObjectiveVersions", x => new { x.FactionId, x.WarId, x.Version });
                table.CheckConstraint("CK_WarObjectiveVersions_FactionId", "\"FactionId\" > 0");
                table.CheckConstraint("CK_WarObjectiveVersions_WarId", "\"WarId\" > 0");
                table.CheckConstraint("CK_WarObjectiveVersions_Version", "\"Version\" > 0");
                table.CheckConstraint("CK_WarObjectiveVersions_Mode", "\"Mode\" BETWEEN 0 AND 2");
                table.CheckConstraint("CK_WarObjectiveVersions_StopScore", "\"StopAtFactionScore\" IS NULL OR \"StopAtFactionScore\" >= 0");
                table.CheckConstraint("CK_WarObjectiveVersions_ChangedBy", "length(btrim(\"ChangedBy\")) > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarObjectiveVersions_FactionId_WarId_Version",
            table: "WarObjectiveVersions",
            columns: new[] { "FactionId", "WarId", "Version" },
            descending: new[] { false, false, true });

        migrationBuilder.Sql("""
            CREATE FUNCTION prevent_war_objective_version_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'WarObjectiveVersions is append-only';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarObjectiveVersions_AppendOnly"
            BEFORE UPDATE OR DELETE ON "WarObjectiveVersions"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_objective_version_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarObjectiveVersions");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_war_objective_version_mutation();");
    }
}
