using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906120000_AddWarPayoutAccounting")]
public partial class AddWarPayoutAccounting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarPayoutPolicyVersions",
            columns: table => new
            {
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                ScoreRate = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                ChainRate = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                AttackRate = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                FixedMemberAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarPayoutPolicyVersions", x => new { x.FactionId, x.WarId, x.Version });
                table.CheckConstraint("CK_WarPayoutPolicyVersions_FactionId", "\"FactionId\" > 0");
                table.CheckConstraint("CK_WarPayoutPolicyVersions_WarId", "\"WarId\" > 0");
                table.CheckConstraint("CK_WarPayoutPolicyVersions_Version", "\"Version\" > 0");
                table.CheckConstraint("CK_WarPayoutPolicyVersions_Rates", "\"ScoreRate\" >= 0 AND \"ScoreRate\" <= 1000000000 AND \"ChainRate\" >= 0 AND \"ChainRate\" <= 1000000000 AND \"AttackRate\" >= 0 AND \"AttackRate\" <= 1000000000");
                table.CheckConstraint("CK_WarPayoutPolicyVersions_Fixed", "\"FixedMemberAmount\" >= 0 AND \"FixedMemberAmount\" <= 1000000000000000");
                table.CheckConstraint("CK_WarPayoutPolicyVersions_ChangedBy", "length(btrim(\"ChangedBy\")) > 0");
            });

        migrationBuilder.CreateTable(
            name: "WarPayoutReconciliations",
            columns: table => new
            {
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                PoolAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                AllocatedAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                UnattributedResidual = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                CalculatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarPayoutReconciliations", x => x.RunId);
                table.CheckConstraint("CK_WarPayoutReconciliations_Amounts", "\"PoolAmount\" >= 0 AND \"PoolAmount\" <= 1000000000000000 AND \"AllocatedAmount\" >= 0 AND \"AllocatedAmount\" <= \"PoolAmount\" AND \"UnattributedResidual\" >= 0 AND \"AllocatedAmount\" + \"UnattributedResidual\" = \"PoolAmount\"");
                table.CheckConstraint("CK_WarPayoutReconciliations_CalculatedBy", "length(btrim(\"CalculatedBy\")) > 0");
                table.ForeignKey(
                    name: "FK_WarPayoutReconciliations_WarAccountingRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "WarAccountingRuns",
                    principalColumn: "RunId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WarPayoutReconciliations_WarPayoutPolicyVersions_Policy",
                    columns: x => new { x.FactionId, x.WarId, x.PolicyVersion },
                    principalTable: "WarPayoutPolicyVersions",
                    principalColumns: new[] { "FactionId", "WarId", "Version" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WarPayoutReconciliations_WarAccountingSourceSnapshots_SourceScope",
                    columns: x => new { x.SourceSnapshotId, x.FactionId, x.WarId },
                    principalTable: "WarAccountingSourceSnapshots",
                    principalColumns: new[] { "SourceSnapshotId", "FactionId", "WarId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarPayoutReconciliations_FactionId_WarId_PolicyVersion",
            table: "WarPayoutReconciliations",
            columns: new[] { "FactionId", "WarId", "PolicyVersion" });
        migrationBuilder.CreateIndex(
            name: "IX_WarPayoutReconciliations_SourceSnapshotId_FactionId_WarId",
            table: "WarPayoutReconciliations",
            columns: new[] { "SourceSnapshotId", "FactionId", "WarId" });

        migrationBuilder.CreateTable(
            name: "WarPayoutLines",
            columns: table => new
            {
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                MemberId = table.Column<long>(type: "bigint", nullable: false),
                MemberName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Score = table.Column<int>(type: "integer", nullable: false),
                Chain = table.Column<int>(type: "integer", nullable: false),
                Attacks = table.Column<int>(type: "integer", nullable: false),
                ScoreAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                ChainAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                AttackAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                FixedAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false),
                TotalAmount = table.Column<decimal>(type: "numeric(24,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarPayoutLines", x => new { x.RunId, x.MemberId });
                table.CheckConstraint("CK_WarPayoutLines_Member", "\"MemberId\" > 0 AND length(btrim(\"MemberName\")) > 0");
                table.CheckConstraint("CK_WarPayoutLines_Facts", "\"Score\" >= 0 AND \"Chain\" >= 0 AND \"Attacks\" >= 0");
                table.CheckConstraint("CK_WarPayoutLines_Amounts", "\"ScoreAmount\" >= 0 AND \"ChainAmount\" >= 0 AND \"AttackAmount\" >= 0 AND \"FixedAmount\" >= 0 AND \"TotalAmount\" = \"ScoreAmount\" + \"ChainAmount\" + \"AttackAmount\" + \"FixedAmount\"");
                table.ForeignKey(
                    name: "FK_WarPayoutLines_WarPayoutReconciliations_RunId",
                    column: x => x.RunId,
                    principalTable: "WarPayoutReconciliations",
                    principalColumn: "RunId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WarPayoutLines_WarAccountingSourceMemberFacts_Beneficiary",
                    columns: x => new { x.SourceSnapshotId, x.MemberId },
                    principalTable: "WarAccountingSourceMemberFacts",
                    principalColumns: new[] { "SourceSnapshotId", "MemberId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarPayoutLines_SourceSnapshotId_MemberId",
            table: "WarPayoutLines",
            columns: new[] { "SourceSnapshotId", "MemberId" });

        migrationBuilder.Sql("""
            CREATE FUNCTION validate_war_payout_reconciliation_scope()
            RETURNS trigger AS $$
            DECLARE
                run_faction bigint;
                run_war bigint;
                run_source uuid;
            BEGIN
                SELECT "FactionId", "WarId", "SourceSnapshotId"
                INTO STRICT run_faction, run_war, run_source
                FROM "WarAccountingRuns"
                WHERE "RunId" = NEW."RunId";

                IF NEW."FactionId" <> run_faction OR NEW."WarId" <> run_war OR NEW."SourceSnapshotId" <> run_source THEN
                    RAISE EXCEPTION 'Payout reconciliation must match its frozen run scope and source';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarPayoutReconciliations_ValidateScope"
            BEFORE INSERT ON "WarPayoutReconciliations"
            FOR EACH ROW EXECUTE FUNCTION validate_war_payout_reconciliation_scope();

            CREATE FUNCTION validate_war_payout_line_scope()
            RETURNS trigger AS $$
            DECLARE
                result_source uuid;
                result_faction bigint;
                result_war bigint;
            BEGIN
                SELECT "SourceSnapshotId", "FactionId", "WarId"
                INTO STRICT result_source, result_faction, result_war
                FROM "WarPayoutReconciliations"
                WHERE "RunId" = NEW."RunId";

                IF NEW."SourceSnapshotId" <> result_source OR NEW."FactionId" <> result_faction OR NEW."WarId" <> result_war THEN
                    RAISE EXCEPTION 'Payout line must match its frozen reconciliation scope and source';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarPayoutLines_ValidateScope"
            BEFORE INSERT ON "WarPayoutLines"
            FOR EACH ROW EXECUTE FUNCTION validate_war_payout_line_scope();

            CREATE FUNCTION prevent_war_payout_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'War payout policy, reconciliation, and lines are append-only';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarPayoutPolicyVersions_Immutable"
            BEFORE UPDATE OR DELETE ON "WarPayoutPolicyVersions"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_payout_mutation();
            CREATE TRIGGER "TR_WarPayoutReconciliations_Immutable"
            BEFORE UPDATE OR DELETE ON "WarPayoutReconciliations"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_payout_mutation();
            CREATE TRIGGER "TR_WarPayoutLines_Immutable"
            BEFORE UPDATE OR DELETE ON "WarPayoutLines"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_payout_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarPayoutLines");
        migrationBuilder.DropTable(name: "WarPayoutReconciliations");
        migrationBuilder.DropTable(name: "WarPayoutPolicyVersions");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS validate_war_payout_line_scope();");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS validate_war_payout_reconciliation_scope();");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_war_payout_mutation();");
    }
}
