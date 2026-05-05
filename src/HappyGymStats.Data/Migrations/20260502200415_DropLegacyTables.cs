using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS DerivedGymTrains");
            migrationBuilder.Sql("DROP TABLE IF EXISTS DerivedHappyEvents");
            migrationBuilder.Sql("DROP TABLE IF EXISTS RawUserLogs");
            migrationBuilder.Sql("DROP TABLE IF EXISTS ImportCheckpoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
