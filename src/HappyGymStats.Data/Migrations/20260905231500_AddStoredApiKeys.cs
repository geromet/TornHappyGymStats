using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260905231500_AddStoredApiKeys")]
public partial class AddStoredApiKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_ConsentRecords_Id_AnonymousId",
            table: "ConsentRecords",
            columns: new[] { "Id", "AnonymousId" });

        migrationBuilder.CreateTable(
            name: "StoredApiKeys",
            columns: table => new
            {
                AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                TornPlayerId = table.Column<int>(type: "integer", nullable: false),
                Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                ConsentRecordId = table.Column<long>(type: "bigint", nullable: false),
                StoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StoredApiKeys", x => x.AnonymousId);
                table.ForeignKey(
                    name: "FK_StoredApiKeys_ConsentRecords_ConsentRecordId_AnonymousId",
                    columns: x => new { x.ConsentRecordId, x.AnonymousId },
                    principalTable: "ConsentRecords",
                    principalColumns: new[] { "Id", "AnonymousId" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StoredApiKeys_IdentityMap_AnonymousId",
                    column: x => x.AnonymousId,
                    principalTable: "IdentityMap",
                    principalColumn: "AnonymousId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StoredApiKeys_ConsentRecordId_AnonymousId",
            table: "StoredApiKeys",
            columns: new[] { "ConsentRecordId", "AnonymousId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StoredApiKeys");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_ConsentRecords_Id_AnonymousId",
            table: "ConsentRecords");
    }
}
