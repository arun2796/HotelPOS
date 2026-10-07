using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase06_Billing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "BillNumbers");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PaymentMethods",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateTable(
                name: "Discounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discounts", x => x.Id);
                    table.CheckConstraint("CK_Discounts_Type", "\"Type\" IN (1, 2)");
                    table.CheckConstraint("CK_Discounts_Value", "\"Value\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCounters",
                columns: table => new
                {
                    Period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCounters", x => x.Period);
                });

            migrationBuilder.CreateTable(
                name: "Bills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillNumber = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('\"BillNumbers\"')"),
                    InvoiceNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    TableCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WaiterId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountId = table.Column<int>(type: "integer", nullable: true),
                    DiscountType = table.Column<int>(type: "integer", nullable: true),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DiscountApprovedBy = table.Column<int>(type: "integer", nullable: true),
                    TaxableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RoundOff = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceChargeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CustomerGstin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClaimedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ClaimedByDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ClaimExpiresAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    SettledBy = table.Column<int>(type: "integer", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bills", x => x.Id);
                    table.CheckConstraint("CK_Bills_Amounts", "\"Subtotal\" >= 0 AND \"DiscountAmount\" >= 0 AND \"GrandTotal\" >= 0 AND \"PaidAmount\" >= 0 AND \"RefundedAmount\" >= 0");
                    table.CheckConstraint("CK_Bills_DiscountType", "\"DiscountType\" IS NULL OR \"DiscountType\" IN (1, 2)");
                    table.CheckConstraint("CK_Bills_PaymentStatus", "\"PaymentStatus\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_Bills_Status", "\"Status\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_Bills_Discounts_DiscountId",
                        column: x => x.DiscountId,
                        principalTable: "Discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bills_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bills_Users_WaiterId",
                        column: x => x.WaiterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    OrderItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ModifiersAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountShare = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillItems", x => x.Id);
                    table.CheckConstraint("CK_BillItems_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_BillItems_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenderedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ChangeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReceivedBy = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundOfPaymentId = table.Column<int>(type: "integer", nullable: true),
                    RefundReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "(\"RefundOfPaymentId\" IS NULL AND \"Amount\" > 0) OR (\"RefundOfPaymentId\" IS NOT NULL AND \"Amount\" < 0)");
                    table.CheckConstraint("CK_Payments_Status", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Payments_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Payments_RefundOfPaymentId",
                        column: x => x.RefundOfPaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillItems_BillId",
                table: "BillItems",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_BillItems_OrderItemId",
                table: "BillItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_BillNumber",
                table: "Bills",
                column: "BillNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bills_DiscountId",
                table: "Bills",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_InvoiceNumber",
                table: "Bills",
                column: "InvoiceNumber",
                unique: true,
                filter: "\"InvoiceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_OrderId_Live",
                table: "Bills",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" <> 4");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_SettledAt",
                table: "Bills",
                column: "SettledAt");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_Status_CreatedAt",
                table: "Bills",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_WaiterId",
                table: "Bills",
                column: "WaiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BillId",
                table: "Payments",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKey",
                table: "Payments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaidAt",
                table: "Payments",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentMethodId",
                table: "Payments",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RefundOfPaymentId",
                table: "Payments",
                column: "RefundOfPaymentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillItems");

            migrationBuilder.DropTable(
                name: "InvoiceCounters");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Bills");

            migrationBuilder.DropTable(
                name: "Discounts");

            migrationBuilder.DropSequence(
                name: "BillNumbers");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PaymentMethods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
