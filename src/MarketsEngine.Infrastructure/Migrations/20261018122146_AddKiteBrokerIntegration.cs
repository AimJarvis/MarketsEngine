using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiteBrokerIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "KiteInstrumentToken",
                table: "Instruments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BrokerSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BrokerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_KiteInstrumentToken",
                table: "Instruments",
                column: "KiteInstrumentToken",
                unique: true,
                filter: "\"KiteInstrumentToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerSessions_Provider",
                table: "BrokerSessions",
                column: "Provider",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerSessions");

            migrationBuilder.DropIndex(
                name: "IX_Instruments_KiteInstrumentToken",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "KiteInstrumentToken",
                table: "Instruments");
        }
    }
}
