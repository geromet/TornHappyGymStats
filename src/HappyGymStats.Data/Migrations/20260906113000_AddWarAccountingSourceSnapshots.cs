using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906113000_AddWarAccountingSourceSnapshots")]
public partial class AddWarAccountingSourceSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarAccountingSourceSnapshots",
            columns: table => new
            {
                SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CapturedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarAccountingSourceSnapshots", x => x.SourceSnapshotId);
                table.UniqueConstraint(
                    "AK_WarAccountingSourceSnapshots_SourceSnapshotId_FactionId_WarId",
                    x => new { x.SourceSnapshotId, x.FactionId, x.WarId });
                table.CheckConstraint("CK_WarAccountingSourceSnapshots_FactionId", "\"FactionId\" > 0");
                table.CheckConstraint("CK_WarAccountingSourceSnapshots_WarId", "\"WarId\" > 0");
                table.CheckConstraint(
                    "CK_WarAccountingSourceSnapshots_Fingerprint",
                    "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("CK_WarAccountingSourceSnapshots_CapturedBy", "length(btrim(\"CapturedBy\")) > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingSourceSnapshots_FactionId_WarId_CapturedAtUtc",
            table: "WarAccountingSourceSnapshots",
            columns: new[] { "FactionId", "WarId", "CapturedAtUtc" });

        migrationBuilder.CreateTable(
            name: "WarAccountingSourceMemberFacts",
            columns: table => new
            {
                SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                MemberId = table.Column<long>(type: "bigint", nullable: false),
                MemberName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Score = table.Column<int>(type: "integer", nullable: false),
                Chain = table.Column<int>(type: "integer", nullable: false),
                Attacks = table.Column<int>(type: "integer", nullable: false),
                CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_WarAccountingSourceMemberFacts",
                    x => new { x.SourceSnapshotId, x.MemberId });
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_FactionId", "\"FactionId\" > 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_WarId", "\"WarId\" > 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_MemberId", "\"MemberId\" > 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_MemberName", "length(btrim(\"MemberName\")) > 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_Score", "\"Score\" >= 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_Chain", "\"Chain\" >= 0");
                table.CheckConstraint("CK_WarAccountingSourceMemberFacts_Attacks", "\"Attacks\" >= 0");
                table.ForeignKey(
                    name: "FK_WarAccountingSourceMemberFacts_WarAccountingSourceSnapshots_SourceScope",
                    columns: x => new { x.SourceSnapshotId, x.FactionId, x.WarId },
                    principalTable: "WarAccountingSourceSnapshots",
                    principalColumns: new[] { "SourceSnapshotId", "FactionId", "WarId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingSourceMemberFacts_SourceSnapshotId_FactionId_WarId",
            table: "WarAccountingSourceMemberFacts",
            columns: new[] { "SourceSnapshotId", "FactionId", "WarId" });

        migrationBuilder.AddColumn<Guid>(
            name: "SourceSnapshotId",
            table: "WarAccountingRuns",
            type: "uuid",
            nullable: false);

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingRuns_SourceSnapshotId_FactionId_WarId",
            table: "WarAccountingRuns",
            columns: new[] { "SourceSnapshotId", "FactionId", "WarId" });

        migrationBuilder.AddForeignKey(
            name: "FK_WarAccountingRuns_WarAccountingSourceSnapshots_SourceScope",
            table: "WarAccountingRuns",
            columns: new[] { "SourceSnapshotId", "FactionId", "WarId" },
            principalTable: "WarAccountingSourceSnapshots",
            principalColumns: new[] { "SourceSnapshotId", "FactionId", "WarId" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE FUNCTION prevent_war_accounting_source_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'War accounting source snapshots and member facts are append-only';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarAccountingSourceSnapshots_Immutable"
            BEFORE UPDATE OR DELETE ON "WarAccountingSourceSnapshots"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_accounting_source_mutation();

            CREATE TRIGGER "TR_WarAccountingSourceMemberFacts_Immutable"
            BEFORE UPDATE OR DELETE ON "WarAccountingSourceMemberFacts"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_accounting_source_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_WarAccountingRuns_WarAccountingSourceSnapshots_SourceScope",
            table: "WarAccountingRuns");

        migrationBuilder.DropIndex(
            name: "IX_WarAccountingRuns_SourceSnapshotId_FactionId_WarId",
            table: "WarAccountingRuns");

        migrationBuilder.DropColumn(
            name: "SourceSnapshotId",
            table: "WarAccountingRuns");

        migrationBuilder.DropTable(name: "WarAccountingSourceMemberFacts");
        migrationBuilder.DropTable(name: "WarAccountingSourceSnapshots");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_war_accounting_source_mutation();");
    }
}
