using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase30_StockReorderLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReorderLevel",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'stock.adjust'), ('Manager', 'stock.adjust'), ('Kitchen', 'stock.adjust'),
                    ('Cashier', 'orders.create')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "Permission" = 'stock.adjust';
                DELETE FROM "RolePermissions" rp USING "Roles" r WHERE rp."RoleId" = r."Id" AND r."Name" = 'Cashier' AND rp."Permission" = 'orders.create';
                """);

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "MenuItems");
        }
    }
}
