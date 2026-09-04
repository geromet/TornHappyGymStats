using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HappyGymStats.Data.Migrations;

[DbContext(typeof(HappyGymStatsDbContext))]
[Migration("20260904170000_AddConsentRecords")]
public partial class AddConsentRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConsentRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AnonymousId = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConsentRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ConsentRecords_AnonymousId_Purpose_DocumentVersion",
            table: "ConsentRecords",
            columns: new[] { "AnonymousId", "Purpose", "DocumentVersion" });

        migrationBuilder.CreateIndex(
            name: "IX_ConsentRecords_AnonymousId_Purpose_RevokedAtUtc",
            table: "ConsentRecords",
            columns: new[] { "AnonymousId", "Purpose", "RevokedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConsentRecords");
    }
}
