using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase28_SplitBillBySeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bills_OrderId_Live",
                table: "Bills");

            migrationBuilder.AddColumn<int>(
                name: "SplitIndex",
                table: "Bills",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SeatCode",
                table: "BillItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bills_OrderId_Live",
                table: "Bills",
                columns: new[] { "OrderId", "SplitIndex" },
                unique: true,
                filter: "\"Status\" <> 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bills_OrderId_Live",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "SplitIndex",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "SeatCode",
                table: "BillItems");

            migrationBuilder.CreateIndex(
                name: "IX_Bills_OrderId_Live",
                table: "Bills",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" <> 4");
        }
    }
}
