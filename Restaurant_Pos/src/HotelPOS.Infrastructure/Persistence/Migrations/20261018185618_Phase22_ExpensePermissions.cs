using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase22_ExpensePermissions : Migration
    {
        /// <inheritdoc />
        // Phase22_Expenses created the tables but did not grant the new permissions to the system roles that already
        // existed, and DbSeeder only assigns defaults to roles it creates. Same pattern as Phase22_Reservations.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'expenses.view'), ('Admin', 'expenses.manage'),
                    ('Manager', 'expenses.view'), ('Manager', 'expenses.manage')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions"
                WHERE "Permission" IN ('expenses.view', 'expenses.manage')
                  AND "RoleId" IN (SELECT "Id" FROM "Roles" WHERE "Name" IN ('Admin', 'Manager'));
                """);
        }
    }
}
