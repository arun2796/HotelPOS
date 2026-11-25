using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase32_ItemPortions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PortionId",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortionName",
                table: "OrderItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PortionQuantity",
                table: "OrderItems",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortionUnit",
                table: "OrderItems",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortionName",
                table: "BillItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PortionQuantity",
                table: "BillItems",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortionUnit",
                table: "BillItems",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MenuItemPortions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MenuItemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemPortions", x => x.Id);
                    table.CheckConstraint("CK_MenuItemPortions_Price", "\"Price\" >= 0");
                    table.CheckConstraint("CK_MenuItemPortions_Quantity", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_MenuItemPortions_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_PortionId",
                table: "OrderItems",
                column: "PortionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemPortions_MenuItemId",
                table: "MenuItemPortions",
                column: "MenuItemId");

            // Every existing item is sold in one portion, "Regular", at the price it has today. Old orders and bills are
            // not touched: their portion columns stay NULL, which reads exactly as before.
            migrationBuilder.Sql(
                "INSERT INTO \"MenuItemPortions\" (\"MenuItemId\", \"Name\", \"Price\", \"IsDefault\", \"IsActive\", \"SortOrder\", \"CreatedAt\") " +
                "SELECT \"Id\", 'Regular', \"Price\", TRUE, TRUE, 1, now() FROM \"MenuItems\";");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MenuItemPortions_PortionId",
                table: "OrderItems",
                column: "PortionId",
                principalTable: "MenuItemPortions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MenuItemPortions_PortionId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "MenuItemPortions");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_PortionId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PortionId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PortionName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PortionQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PortionUnit",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PortionName",
                table: "BillItems");

            migrationBuilder.DropColumn(
                name: "PortionQuantity",
                table: "BillItems");

            migrationBuilder.DropColumn(
                name: "PortionUnit",
                table: "BillItems");
        }
    }
}
