using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedHappyEventParityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClampedToMax",
                table: "DerivedHappyEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HappyUsed",
                table: "DerivedHappyEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxHappyAtTimeUtc",
                table: "DerivedHappyEvents",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClampedToMax",
                table: "DerivedHappyEvents");

            migrationBuilder.DropColumn(
                name: "HappyUsed",
                table: "DerivedHappyEvents");

            migrationBuilder.DropColumn(
                name: "MaxHappyAtTimeUtc",
                table: "DerivedHappyEvents");
        }
    }
}
