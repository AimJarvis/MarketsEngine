using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionStopLossTakeProfitOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
                table: "PaperPositions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "PaperPositions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
                table: "LivePositions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "LivePositions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
                table: "LiveOrders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "LiveOrders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StopLossPercent",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "TakeProfitPercent",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "StopLossPercent",
                table: "LivePositions");

            migrationBuilder.DropColumn(
                name: "TakeProfitPercent",
                table: "LivePositions");

            migrationBuilder.DropColumn(
                name: "StopLossPercent",
                table: "LiveOrders");

            migrationBuilder.DropColumn(
                name: "TakeProfitPercent",
                table: "LiveOrders");
        }
    }
}
