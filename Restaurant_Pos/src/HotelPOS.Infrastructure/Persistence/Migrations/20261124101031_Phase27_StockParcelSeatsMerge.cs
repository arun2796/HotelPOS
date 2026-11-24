using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase27_StockParcelSeatsMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MergedIntoTableId",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsParcel",
                table: "OrderItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingCharge",
                table: "OrderItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SeatCode",
                table: "OrderItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingCharge",
                table: "MenuItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StockMode",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StockOpening",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockRemaining",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tables_MergedIntoTableId",
                table: "Tables",
                column: "MergedIntoTableId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MenuItems_PackagingCharge",
                table: "MenuItems",
                sql: "\"PackagingCharge\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MenuItems_StockMode",
                table: "MenuItems",
                sql: "\"StockMode\" IN (0, 1, 2)");

            migrationBuilder.AddForeignKey(
                name: "FK_Tables_Tables_MergedIntoTableId",
                table: "Tables",
                column: "MergedIntoTableId",
                principalTable: "Tables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The client's two kitchen sources. An upgraded venue database already has Main Kitchen: add Live Kitchen
            // (tandoor, griddle, made-to-order) next to it. A new database is empty here (the seeder creates both).
            migrationBuilder.Sql("""
                INSERT INTO "PreparationStations" ("Name", "Code", "SortOrder", "IsActive", "CreatedAt")
                SELECT 'Live Kitchen', 'LIVE', 2, TRUE, NOW() AT TIME ZONE 'utc'
                WHERE EXISTS (SELECT 1 FROM "PreparationStations")
                  AND NOT EXISTS (SELECT 1 FROM "PreparationStations" WHERE "Code" = 'LIVE' OR "Name" = 'Live Kitchen');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tables_Tables_MergedIntoTableId",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_Tables_MergedIntoTableId",
                table: "Tables");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MenuItems_PackagingCharge",
                table: "MenuItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MenuItems_StockMode",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "MergedIntoTableId",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "IsParcel",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PackagingCharge",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SeatCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PackagingCharge",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "StockMode",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "StockOpening",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "StockRemaining",
                table: "MenuItems");
        }
    }
}
