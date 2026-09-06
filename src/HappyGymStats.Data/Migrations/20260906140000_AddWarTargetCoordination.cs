using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260906140000_AddWarTargetCoordination")]
public partial class AddWarTargetCoordination : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WarTargetClaims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                TargetMemberId = table.Column<long>(type: "bigint", nullable: false),
                AttackerMemberId = table.Column<long>(type: "bigint", nullable: false),
                ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Mode = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarTargetClaims", x => x.Id);
                table.CheckConstraint("CK_WarTargetClaims_Scope", "\"FactionId\" > 0 AND \"WarId\" > 0");
                table.CheckConstraint("CK_WarTargetClaims_Members", "\"TargetMemberId\" > 0 AND \"AttackerMemberId\" > 0");
                table.CheckConstraint("CK_WarTargetClaims_Window", "\"ExpiresAtUtc\" > \"ClaimedAtUtc\"");
                table.CheckConstraint("CK_WarTargetClaims_Mode", "\"Mode\" IN (0, 1)");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarTargetClaims_Scope_Target_Expiry",
            table: "WarTargetClaims",
            columns: new[] { "FactionId", "WarId", "TargetMemberId", "ExpiresAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_WarTargetClaims_Scope_Attacker_Expiry",
            table: "WarTargetClaims",
            columns: new[] { "FactionId", "WarId", "AttackerMemberId", "ExpiresAtUtc" });

        migrationBuilder.CreateTable(
            name: "WarTargetReservations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FactionId = table.Column<long>(type: "bigint", nullable: false),
                WarId = table.Column<long>(type: "bigint", nullable: false),
                TargetMemberId = table.Column<long>(type: "bigint", nullable: false),
                ActivatesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PrimaryAttackerMemberId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarTargetReservations", x => x.Id);
                table.CheckConstraint("CK_WarTargetReservations_Scope", "\"FactionId\" > 0 AND \"WarId\" > 0");
                table.CheckConstraint("CK_WarTargetReservations_Members", "\"TargetMemberId\" > 0 AND \"PrimaryAttackerMemberId\" > 0");
                table.CheckConstraint("CK_WarTargetReservations_Window", "\"ExpiresAtUtc\" > \"ActivatesAtUtc\"");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarTargetReservations_Scope_Target_Activation",
            table: "WarTargetReservations",
            columns: new[] { "FactionId", "WarId", "TargetMemberId", "ActivatesAtUtc" });

        migrationBuilder.CreateTable(
            name: "WarTargetReservationCandidates",
            columns: table => new
            {
                ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                Ordinal = table.Column<int>(type: "integer", nullable: false),
                AttackerMemberId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarTargetReservationCandidates", x => new { x.ReservationId, x.Ordinal });
                table.ForeignKey(
                    name: "FK_WarTargetReservationCandidates_WarTargetReservations_ReservationId",
                    column: x => x.ReservationId,
                    principalTable: "WarTargetReservations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("CK_WarTargetReservationCandidates_Ordinal", "\"Ordinal\" >= 0");
                table.CheckConstraint("CK_WarTargetReservationCandidates_Attacker", "\"AttackerMemberId\" > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarTargetReservationCandidates_Reservation_Attacker",
            table: "WarTargetReservationCandidates",
            columns: new[] { "ReservationId", "AttackerMemberId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WarTargetReservationCandidates");
        migrationBuilder.DropTable(name: "WarTargetClaims");
        migrationBuilder.DropTable(name: "WarTargetReservations");
    }
}
