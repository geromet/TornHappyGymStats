using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModifierProvenanceModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModifierProvenance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DerivedGymTrainLogId = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: true),
                    FactionId = table.Column<string>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<string>(type: "TEXT", nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModifierProvenance", x => x.Id);
                    table.CheckConstraint("CK_ModifierProvenance_CompanyRequired", "Scope <> 'company' OR (CompanyId IS NOT NULL AND length(trim(CompanyId)) > 0)");
                    table.CheckConstraint("CK_ModifierProvenance_FactionRequired", "Scope <> 'faction' OR (FactionId IS NOT NULL AND length(trim(FactionId)) > 0)");
                    table.CheckConstraint("CK_ModifierProvenance_Scope", "Scope IN ('personal', 'faction', 'company')");
                    table.CheckConstraint("CK_ModifierProvenance_SubjectRequired", "Scope <> 'personal' OR (SubjectId IS NOT NULL AND length(trim(SubjectId)) > 0)");
                    table.CheckConstraint("CK_ModifierProvenance_VerificationStatus", "VerificationStatus IN ('verified', 'unresolved', 'unavailable')");
                    table.ForeignKey(
                        name: "FK_ModifierProvenance_DerivedGymTrains_DerivedGymTrainLogId",
                        column: x => x.DerivedGymTrainLogId,
                        principalTable: "DerivedGymTrains",
                        principalColumn: "LogId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModifierProvenance_DerivedGymTrainLogId_Scope",
                table: "ModifierProvenance",
                columns: new[] { "DerivedGymTrainLogId", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModifierProvenance_Scope_ValidFromUtc_ValidToUtc",
                table: "ModifierProvenance",
                columns: new[] { "Scope", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ModifierProvenance_VerificationStatus",
                table: "ModifierProvenance",
                column: "VerificationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModifierProvenance");
        }
    }
}
