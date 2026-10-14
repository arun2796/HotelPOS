using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_Gst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HsnSac",
                table: "MenuItems",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerLegalName",
                table: "Bills",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerStateCode",
                table: "Bills",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfSupply",
                table: "Bills",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplyType",
                table: "Bills",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PeriodCloses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    ClosedBy = table.Column<int>(type: "integer", nullable: false),
                    Checks = table.Column<string>(type: "jsonb", nullable: false),
                    PackFolder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ReopenedBy = table.Column<int>(type: "integer", nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodCloses", x => x.Id);
                    table.CheckConstraint("CK_PeriodCloses_Kind", "\"Kind\" IN (0, 1)");
                    table.CheckConstraint("CK_PeriodCloses_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_PeriodCloses_Users_ClosedBy",
                        column: x => x.ClosedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodCloses_Users_ReopenedBy",
                        column: x => x.ReopenedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCloses_ClosedBy",
                table: "PeriodCloses",
                column: "ClosedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCloses_Kind_PeriodStart",
                table: "PeriodCloses",
                columns: new[] { "Kind", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCloses_ReopenedBy",
                table: "PeriodCloses",
                column: "ReopenedBy");

            // Existing databases keep their roles, and DbSeeder only grants defaults to roles it creates, so the new
            // permissions are granted here (the Phase 15 defaults, frozen on purpose): GST reports to managers, period
            // close to administrators. ON CONFLICT keeps reruns safe.
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'gst.view'), ('Admin', 'period.close'),
                    ('Manager', 'gst.view')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "Permission" IN ('gst.view', 'period.close');
                """);

            migrationBuilder.DropTable(
                name: "PeriodCloses");

            migrationBuilder.DropColumn(
                name: "HsnSac",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "CustomerLegalName",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "CustomerStateCode",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "PlaceOfSupply",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "SupplyType",
                table: "Bills");
        }
    }
}
