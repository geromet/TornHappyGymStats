using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedPiiColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedTornPlayerId",
                table: "IdentityMap",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PublicKey",
                table: "IdentityMap",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedAffiliationId",
                table: "AffiliationEvents",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedTornPlayerId",
                table: "IdentityMap");

            migrationBuilder.DropColumn(
                name: "PublicKey",
                table: "IdentityMap");

            migrationBuilder.DropColumn(
                name: "EncryptedAffiliationId",
                table: "AffiliationEvents");
        }
    }
}
