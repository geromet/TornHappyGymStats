using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModifierProvenance_DerivedGymTrains_DerivedGymTrainLogId",
                table: "ModifierProvenance");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ModifierProvenance",
                table: "ModifierProvenance");

            migrationBuilder.DropIndex(
                name: "IX_ModifierProvenance_DerivedGymTrainLogId_Scope",
                table: "ModifierProvenance");

            migrationBuilder.DropIndex(
                name: "IX_ModifierProvenance_Scope_ValidFromUtc_ValidToUtc",
                table: "ModifierProvenance");

            migrationBuilder.DropIndex(
                name: "IX_ModifierProvenance_VerificationStatus",
                table: "ModifierProvenance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModifierProvenance_CompanyRequired",
                table: "ModifierProvenance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModifierProvenance_FactionRequired",
                table: "ModifierProvenance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModifierProvenance_Scope",
                table: "ModifierProvenance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModifierProvenance_SubjectRequired",
                table: "ModifierProvenance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModifierProvenance_VerificationStatus",
                table: "ModifierProvenance");

            migrationBuilder.DropColumn(
                name: "DerivedGymTrainLogId",
                table: "ModifierProvenance");

            migrationBuilder.DropColumn(
                name: "ValidFromUtc",
                table: "ModifierProvenance");

            migrationBuilder.DropColumn(
                name: "ValidToUtc",
                table: "ModifierProvenance");

            migrationBuilder.DropColumn(
                name: "VerificationDetails",
                table: "ModifierProvenance");

            migrationBuilder.RenameColumn(
                name: "VerificationReasonCode",
                table: "ModifierProvenance",
                newName: "LogEntryId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ModifierProvenance",
                newName: "PlayerId");

            migrationBuilder.AddColumn<int>(
                name: "HappyDecreased",
                table: "UserLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HappyIncreased",
                table: "UserLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "VerificationStatus",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "SubjectId",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "FactionId",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModifierProvenance",
                table: "ModifierProvenance",
                columns: new[] { "PlayerId", "LogEntryId", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_ModifierProvenance_PlayerId_VerificationStatus",
                table: "ModifierProvenance",
                columns: new[] { "PlayerId", "VerificationStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ModifierProvenance",
                table: "ModifierProvenance");

            migrationBuilder.DropIndex(
                name: "IX_ModifierProvenance_PlayerId_VerificationStatus",
                table: "ModifierProvenance");

            migrationBuilder.DropColumn(
                name: "HappyDecreased",
                table: "UserLogEntries");

            migrationBuilder.DropColumn(
                name: "HappyIncreased",
                table: "UserLogEntries");

            migrationBuilder.RenameColumn(
                name: "LogEntryId",
                table: "ModifierProvenance",
                newName: "VerificationReasonCode");

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "ModifierProvenance",
                newName: "Id");

            migrationBuilder.AlterColumn<string>(
                name: "VerificationStatus",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "SubjectId",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FactionId",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CompanyId",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "ModifierProvenance",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<string>(
                name: "DerivedGymTrainLogId",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidFromUtc",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidToUtc",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationDetails",
                table: "ModifierProvenance",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ModifierProvenance",
                table: "ModifierProvenance",
                column: "Id");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModifierProvenance_CompanyRequired",
                table: "ModifierProvenance",
                sql: "Scope <> 'company' OR (CompanyId IS NOT NULL AND length(trim(CompanyId)) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModifierProvenance_FactionRequired",
                table: "ModifierProvenance",
                sql: "Scope <> 'faction' OR (FactionId IS NOT NULL AND length(trim(FactionId)) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModifierProvenance_Scope",
                table: "ModifierProvenance",
                sql: "Scope IN ('personal', 'faction', 'company')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModifierProvenance_SubjectRequired",
                table: "ModifierProvenance",
                sql: "Scope <> 'personal' OR (SubjectId IS NOT NULL AND length(trim(SubjectId)) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModifierProvenance_VerificationStatus",
                table: "ModifierProvenance",
                sql: "VerificationStatus IN ('verified', 'unresolved', 'unavailable')");

            migrationBuilder.AddForeignKey(
                name: "FK_ModifierProvenance_DerivedGymTrains_DerivedGymTrainLogId",
                table: "ModifierProvenance",
                column: "DerivedGymTrainLogId",
                principalTable: "DerivedGymTrains",
                principalColumn: "LogId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
