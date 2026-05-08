using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityMap",
                columns: table => new
                {
                    AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeycloakSub = table.Column<string>(type: "text", nullable: true),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityMap", x => x.AnonymousId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityMap_KeycloakSub",
                table: "IdentityMap",
                column: "KeycloakSub",
                unique: true,
                filter: "\"KeycloakSub\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityMap");
        }
    }
}
