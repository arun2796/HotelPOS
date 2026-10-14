using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase16_LicensingImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StateCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    AppliedBy = table.Column<int>(type: "integer", nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UndoneBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatches_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_ImportBatches_Status", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ImportBatches_Users_AppliedBy",
                        column: x => x.AppliedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Users_UndoneBy",
                        column: x => x.UndoneBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerLicense",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TrialStartedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    Key = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    AppliedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerLicense", x => x.Id);
                    table.CheckConstraint("CK_ServerLicense_Singleton", "\"Id\" = 1");
                    table.ForeignKey(
                        name: "FK_ServerLicense_Users_AppliedBy",
                        column: x => x.AppliedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchId = table.Column<int>(type: "integer", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportEntries", x => x.Id);
                    table.CheckConstraint("CK_ImportEntries_Action", "\"Action\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ImportEntries_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AppliedBy",
                table: "ImportBatches",
                column: "AppliedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UndoneBy",
                table: "ImportBatches",
                column: "UndoneBy");

            migrationBuilder.CreateIndex(
                name: "IX_ImportEntries_BatchId_Seq",
                table: "ImportEntries",
                columns: new[] { "BatchId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerLicense_AppliedBy",
                table: "ServerLicense",
                column: "AppliedBy");

            // The 7-day trial starts when the database is first migrated (a new install, or an upgrade to Phase 16).
            migrationBuilder.Sql("""
                INSERT INTO "ServerLicense" ("Id", "TrialStartedAt") VALUES (1, now()) ON CONFLICT DO NOTHING;
                """);

            // Existing databases keep their roles (DbSeeder only grants defaults to roles it creates), so the Phase 16
            // permissions are granted here: licence management to administrators, import to administrators and managers.
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'license.manage'), ('Admin', 'import.run'),
                    ('Manager', 'import.run')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "Permission" IN ('license.manage', 'import.run');
                """);

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "ImportEntries");

            migrationBuilder.DropTable(
                name: "ServerLicense");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
