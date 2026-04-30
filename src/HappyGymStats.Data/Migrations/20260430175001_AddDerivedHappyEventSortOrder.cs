using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedHappyEventSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SortOrder",
                table: "DerivedHappyEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_DerivedHappyEvents_SortOrder",
                table: "DerivedHappyEvents",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DerivedHappyEvents_SortOrder",
                table: "DerivedHappyEvents");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "DerivedHappyEvents");
        }
    }
}
