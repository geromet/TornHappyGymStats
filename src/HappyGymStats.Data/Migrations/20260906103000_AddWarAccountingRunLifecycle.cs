using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906103000_AddWarAccountingRunLifecycle")]
public partial class AddWarAccountingRunLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarAccountingRunLifecycleEvents",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                SupersedingRunId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarAccountingRunLifecycleEvents", x => x.EventId);
                table.CheckConstraint("CK_WarAccountingRunLifecycleEvents_Kind", "\"Kind\" IN (1, 2)");
                table.CheckConstraint("CK_WarAccountingRunLifecycleEvents_Actor", "length(btrim(\"Actor\")) > 0");
                table.CheckConstraint("CK_WarAccountingRunLifecycleEvents_Reason", "length(btrim(\"Reason\")) > 0");
                table.CheckConstraint(
                    "CK_WarAccountingRunLifecycleEvents_SupersedingShape",
                    "(\"Kind\" = 1 AND \"SupersedingRunId\" IS NULL) OR (\"Kind\" = 2 AND \"SupersedingRunId\" IS NOT NULL AND \"SupersedingRunId\" <> \"RunId\")");
                table.ForeignKey(
                    name: "FK_WarAccountingRunLifecycleEvents_WarAccountingRuns_RunId",
                    column: x => x.RunId,
                    principalTable: "WarAccountingRuns",
                    principalColumn: "RunId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_WarAccountingRunLifecycleEvents_WarAccountingRuns_SupersedingRunId",
                    column: x => x.SupersedingRunId,
                    principalTable: "WarAccountingRuns",
                    principalColumn: "RunId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingRunLifecycleEvents_RunId_Kind",
            table: "WarAccountingRunLifecycleEvents",
            columns: new[] { "RunId", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WarAccountingRunLifecycleEvents_SupersedingRunId",
            table: "WarAccountingRunLifecycleEvents",
            column: "SupersedingRunId");

        migrationBuilder.Sql("""
            CREATE FUNCTION validate_war_accounting_run_lifecycle()
            RETURNS trigger AS $$
            DECLARE
                source_faction bigint;
                source_war bigint;
                source_frozen_at timestamptz;
                replacement_faction bigint;
                replacement_war bigint;
                replacement_frozen_at timestamptz;
                source_approved_at timestamptz;
                replacement_approved_at timestamptz;
            BEGIN
                SELECT "FactionId", "WarId", "FrozenAtUtc"
                INTO STRICT source_faction, source_war, source_frozen_at
                FROM "WarAccountingRuns"
                WHERE "RunId" = NEW."RunId";

                IF NEW."OccurredAtUtc" < source_frozen_at THEN
                    RAISE EXCEPTION 'Accounting lifecycle event cannot predate run freeze';
                END IF;

                IF NEW."Kind" = 1 THEN
                    IF EXISTS (
                        SELECT 1 FROM "WarAccountingRunLifecycleEvents"
                        WHERE "RunId" = NEW."RunId" AND "Kind" = 2) THEN
                        RAISE EXCEPTION 'A superseded accounting run cannot be approved again';
                    END IF;
                    RETURN NEW;
                END IF;

                SELECT "OccurredAtUtc"
                INTO source_approved_at
                FROM "WarAccountingRunLifecycleEvents"
                WHERE "RunId" = NEW."RunId" AND "Kind" = 1;

                IF source_approved_at IS NULL THEN
                    RAISE EXCEPTION 'Only an approved accounting run can be superseded';
                END IF;

                SELECT "FactionId", "WarId", "FrozenAtUtc"
                INTO STRICT replacement_faction, replacement_war, replacement_frozen_at
                FROM "WarAccountingRuns"
                WHERE "RunId" = NEW."SupersedingRunId";

                IF replacement_faction <> source_faction OR replacement_war <> source_war THEN
                    RAISE EXCEPTION 'Superseding accounting run must have the same faction and war scope';
                END IF;

                SELECT "OccurredAtUtc"
                INTO replacement_approved_at
                FROM "WarAccountingRunLifecycleEvents"
                WHERE "RunId" = NEW."SupersedingRunId" AND "Kind" = 1;

                IF replacement_approved_at IS NULL THEN
                    RAISE EXCEPTION 'Superseding accounting run must already be approved';
                END IF;

                IF NEW."OccurredAtUtc" < source_approved_at
                   OR NEW."OccurredAtUtc" < replacement_approved_at
                   OR NEW."OccurredAtUtc" < replacement_frozen_at THEN
                    RAISE EXCEPTION 'Supersession cannot predate either approval or replacement freeze';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarAccountingRunLifecycleEvents_Validate"
            BEFORE INSERT ON "WarAccountingRunLifecycleEvents"
            FOR EACH ROW EXECUTE FUNCTION validate_war_accounting_run_lifecycle();

            CREATE FUNCTION prevent_war_accounting_run_lifecycle_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'WarAccountingRunLifecycleEvents is append-only';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER "TR_WarAccountingRunLifecycleEvents_Immutable"
            BEFORE UPDATE OR DELETE ON "WarAccountingRunLifecycleEvents"
            FOR EACH ROW EXECUTE FUNCTION prevent_war_accounting_run_lifecycle_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarAccountingRunLifecycleEvents");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS validate_war_accounting_run_lifecycle();");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_war_accounting_run_lifecycle_mutation();");
    }
}
