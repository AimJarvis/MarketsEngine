using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalBars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalBars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalBars_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalBars_InstrumentId_TimestampUtc_Interval",
                table: "HistoricalBars",
                columns: new[] { "InstrumentId", "TimestampUtc", "Interval" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalBars");
        }
    }
}
