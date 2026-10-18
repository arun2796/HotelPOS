using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase22_Reservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableId = table.Column<int>(type: "integer", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PartySize = table.Column<int>(type: "integer", nullable: false),
                    StartTimeUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    EndTimeUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SeatedAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_PartySize", "\"PartySize\" BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_Reservations_Status", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_Reservations_TimeRange", "\"EndTimeUtc\" > \"StartTimeUtc\"");
                    table.ForeignKey(
                        name: "FK_Reservations_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Tables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_StartTimeUtc",
                table: "Reservations",
                column: "StartTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TableId",
                table: "Reservations",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TableId_Status_StartTimeUtc_EndTimeUtc",
                table: "Reservations",
                columns: new[] { "TableId", "Status", "StartTimeUtc", "EndTimeUtc" });

            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'reservations.view'), ('Admin', 'reservations.manage'),
                    ('Manager', 'reservations.view'), ('Manager', 'reservations.manage'),
                    ('Waiter', 'reservations.view'), ('Waiter', 'reservations.manage'),
                    ('Cashier', 'reservations.view'), ('Cashier', 'reservations.manage')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions"
                WHERE "Permission" IN ('reservations.view', 'reservations.manage');
                """);

            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
