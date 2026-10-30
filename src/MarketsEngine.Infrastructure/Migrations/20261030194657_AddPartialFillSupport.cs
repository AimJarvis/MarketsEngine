using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialFillSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LiveFills_OrderId",
                table: "LiveFills");

            migrationBuilder.AddColumn<int>(
                name: "CumulativeFilledQuantityAfter",
                table: "LiveFills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LiveFills_OrderId_CumulativeFilledQuantityAfter",
                table: "LiveFills",
                columns: new[] { "OrderId", "CumulativeFilledQuantityAfter" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LiveFills_OrderId_CumulativeFilledQuantityAfter",
                table: "LiveFills");

            migrationBuilder.DropColumn(
                name: "CumulativeFilledQuantityAfter",
                table: "LiveFills");

            migrationBuilder.CreateIndex(
                name: "IX_LiveFills_OrderId",
                table: "LiveFills",
                column: "OrderId",
                unique: true);
        }
    }
}
