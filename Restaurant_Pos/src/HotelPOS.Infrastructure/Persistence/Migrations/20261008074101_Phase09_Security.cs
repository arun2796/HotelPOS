using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase09_Security : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "Devices",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Databases created before Phase 9 already have their roles, so DbSeeder (which only seeds roles it creates)
            // would leave them without permissions. The lists are the Phase 9 defaults, frozen here on purpose.
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'users.manage'), ('Admin', 'roles.manage'), ('Admin', 'settings.manage'),
                    ('Admin', 'devices.manage'), ('Admin', 'audit.view'), ('Admin', 'backup.manage'),
                    ('Admin', 'tables.view'), ('Admin', 'tables.operate'), ('Admin', 'tables.manage'),
                    ('Admin', 'menu.view'), ('Admin', 'menu.manage'), ('Admin', 'menu.availability'),
                    ('Admin', 'orders.create'), ('Admin', 'orders.edit.any'), ('Admin', 'orders.cancel.any'),
                    ('Admin', 'kitchen.operate'),
                    ('Admin', 'billing.operate'), ('Admin', 'billing.discount'), ('Admin', 'billing.void'), ('Admin', 'billing.refund'),
                    ('Admin', 'reports.view'),
                    ('Manager', 'audit.view'),
                    ('Manager', 'tables.view'), ('Manager', 'tables.operate'), ('Manager', 'tables.manage'),
                    ('Manager', 'menu.view'), ('Manager', 'menu.manage'), ('Manager', 'menu.availability'),
                    ('Manager', 'orders.create'), ('Manager', 'orders.edit.any'), ('Manager', 'orders.cancel.any'),
                    ('Manager', 'kitchen.operate'),
                    ('Manager', 'billing.operate'), ('Manager', 'billing.discount'), ('Manager', 'billing.void'), ('Manager', 'billing.refund'),
                    ('Manager', 'reports.view'),
                    ('Waiter', 'tables.view'), ('Waiter', 'tables.operate'), ('Waiter', 'menu.view'), ('Waiter', 'orders.create'),
                    ('Kitchen', 'menu.view'), ('Kitchen', 'menu.availability'), ('Kitchen', 'kitchen.operate'),
                    ('Cashier', 'tables.view'), ('Cashier', 'tables.operate'), ('Cashier', 'menu.view'),
                    ('Cashier', 'billing.operate'), ('Cashier', 'billing.discount')
                ) AS p(role, permission) ON p.role = r."Name";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "Devices");
        }
    }
}
