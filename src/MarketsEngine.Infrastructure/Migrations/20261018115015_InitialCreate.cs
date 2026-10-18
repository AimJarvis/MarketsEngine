using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingSymbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Underlying = table.Column<int>(type: "integer", nullable: false),
                    OptionType = table.Column<int>(type: "integer", nullable: false),
                    StrikePrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LotSize = table.Column<int>(type: "integer", nullable: false),
                    DataSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                    table.CheckConstraint("CK_Instrument_LotSize_Positive", "\"LotSize\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "PaperAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CashBalance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservedMargin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperAccounts", x => x.Id);
                    table.CheckConstraint("CK_PaperAccount_ReservedMargin_NotNegative", "\"ReservedMargin\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BidPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AskPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LastTradedPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DataSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketQuotes", x => x.Id);
                    table.CheckConstraint("CK_MarketQuote_Ask_Gte_Bid", "\"AskPrice\" >= \"BidPrice\"");
                    table.ForeignKey(
                        name: "FK_MarketQuotes_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RelatedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashLedgerEntries_PaperAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    TradingStyle = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperOrders", x => x.Id);
                    table.CheckConstraint("CK_PaperOrder_Quantity_Positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_PaperOrders_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperOrders_PaperAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetQuantity = table.Column<int>(type: "integer", nullable: false),
                    AverageEntryPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedPnL = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperPositions_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperPositions_PaperAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "PaperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperFills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilledQuantity = table.Column<int>(type: "integer", nullable: false),
                    FillPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SlippageAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FilledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperFills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperFills_PaperOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaperOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgerEntries_AccountId_CreatedAtUtc",
                table: "CashLedgerEntries",
                columns: new[] { "AccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_TradingSymbol",
                table: "Instruments",
                column: "TradingSymbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketQuotes_InstrumentId_QuotedAtUtc",
                table: "MarketQuotes",
                columns: new[] { "InstrumentId", "QuotedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperFills_OrderId",
                table: "PaperFills",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_AccountId_RequestedAtUtc",
                table: "PaperOrders",
                columns: new[] { "AccountId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_IdempotencyKey",
                table: "PaperOrders",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperOrders_InstrumentId",
                table: "PaperOrders",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperPositions_AccountId_InstrumentId",
                table: "PaperPositions",
                columns: new[] { "AccountId", "InstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperPositions_InstrumentId",
                table: "PaperPositions",
                column: "InstrumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashLedgerEntries");

            migrationBuilder.DropTable(
                name: "MarketQuotes");

            migrationBuilder.DropTable(
                name: "PaperFills");

            migrationBuilder.DropTable(
                name: "PaperPositions");

            migrationBuilder.DropTable(
                name: "PaperOrders");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "PaperAccounts");
        }
    }
}
