using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase22_FloorLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosX",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosY",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rotation",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Shape",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tables_Layout",
                table: "Tables",
                sql: "(\"PosX\" IS NULL AND \"PosY\" IS NULL AND \"Width\" IS NULL AND \"Height\" IS NULL AND \"Shape\" IS NULL AND \"Rotation\" IS NULL) OR (\"PosX\" BETWEEN 0 AND 5000 AND \"PosY\" BETWEEN 0 AND 5000 AND \"Width\" BETWEEN 40 AND 600 AND \"Height\" BETWEEN 40 AND 600 AND \"Shape\" IN (1, 2) AND \"Rotation\" BETWEEN 0 AND 359)");

            // Default grid so an existing venue sees a usable map straight away: per section, active tables ordered
            // by Code, 6 per row, 120x80 rectangles, 24 unit gap and margin. Tables added later start unplaced.
            migrationBuilder.Sql("""
                UPDATE "Tables" t
                SET "PosX" = 24 + (g.n % 6) * 144,
                    "PosY" = 24 + (g.n / 6) * 104,
                    "Width" = 120,
                    "Height" = 80,
                    "Shape" = 1,
                    "Rotation" = 0
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "SectionId" ORDER BY "Code") - 1 AS n
                    FROM "Tables"
                    WHERE "IsActive"
                ) g
                WHERE t."Id" = g."Id";
                """);

            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", 'floor.layout.manage'
                FROM "Roles" r
                WHERE r."Name" = 'Admin'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "Permission" = 'floor.layout.manage';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tables_Layout",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "PosX",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "PosY",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Rotation",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Shape",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Tables");
        }
    }
}
