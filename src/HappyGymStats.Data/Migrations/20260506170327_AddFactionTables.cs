using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFactionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FactionIdMap",
                columns: table => new
                {
                    AffiliationId = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    FactionAnonymousId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactionIdMap", x => x.AffiliationId);
                });

            migrationBuilder.CreateTable(
                name: "FactionMembership",
                columns: table => new
                {
                    FactionAnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberAnonymousId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactionMembership", x => new { x.FactionAnonymousId, x.MemberAnonymousId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactionIdMap_FactionAnonymousId",
                table: "FactionIdMap",
                column: "FactionAnonymousId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FactionMembership_MemberAnonymousId",
                table: "FactionMembership",
                column: "MemberAnonymousId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactionIdMap");

            migrationBuilder.DropTable(
                name: "FactionMembership");
        }
    }
}
