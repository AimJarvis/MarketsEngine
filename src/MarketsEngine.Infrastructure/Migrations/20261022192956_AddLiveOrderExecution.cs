using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveOrderExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BrokerOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveOrders", x => x.Id);
                    table.CheckConstraint("CK_LiveOrder_Quantity_Positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_LiveOrders_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LivePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetQuantity = table.Column<int>(type: "integer", nullable: false),
                    AverageEntryPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedPnL = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LivePositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LivePositions_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiveFills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilledQuantity = table.Column<int>(type: "integer", nullable: false),
                    FillPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FilledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveFills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveFills_LiveOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "LiveOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveFills_OrderId",
                table: "LiveFills",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveOrders_IdempotencyKey",
                table: "LiveOrders",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveOrders_InstrumentId",
                table: "LiveOrders",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveOrders_RequestedAtUtc",
                table: "LiveOrders",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LiveOrders_Status",
                table: "LiveOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LivePositions_InstrumentId",
                table: "LivePositions",
                column: "InstrumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveFills");

            migrationBuilder.DropTable(
                name: "LivePositions");

            migrationBuilder.DropTable(
                name: "LiveOrders");
        }
    }
}
