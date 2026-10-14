using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13_CashAndDayEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Backups_Trigger",
                table: "Backups");

            migrationBuilder.AddColumn<int>(
                name: "CashShiftId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessDayCloses",
                columns: table => new
                {
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    ClosedBy = table.Column<int>(type: "integer", nullable: false),
                    Overrides = table.Column<string>(type: "jsonb", nullable: false),
                    TotalsSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    BackupFile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PackFolder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ReopenedBy = table.Column<int>(type: "integer", nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDayCloses", x => x.BusinessDay);
                    table.CheckConstraint("CK_BusinessDayCloses_Status", "\"Status\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_BusinessDayCloses_Users_ClosedBy",
                        column: x => x.ClosedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessDayCloses_Users_ReopenedBy",
                        column: x => x.ReopenedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    CashierUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    OpenedBy = table.Column<int>(type: "integer", nullable: false),
                    FloatAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ClosedBy = table.Column<int>(type: "integer", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Variance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    VarianceReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    VarianceApprovedBy = table.Column<int>(type: "integer", nullable: true),
                    BlindCount = table.Column<bool>(type: "boolean", nullable: false),
                    HandedOverToShiftId = table.Column<int>(type: "integer", nullable: true),
                    ForcedReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                    table.CheckConstraint("CK_CashShifts_Float", "\"FloatAmount\" >= 0");
                    table.CheckConstraint("CK_CashShifts_Status", "\"Status\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_CashShifts_CashShifts_HandedOverToShiftId",
                        column: x => x.HandedOverToShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShifts_Users_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Denominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Denominations", x => x.Id);
                    table.CheckConstraint("CK_Denominations_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_Denominations_Value", "\"Value\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "NonTillCollections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RecordedBy = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonTillCollections", x => x.Id);
                    table.CheckConstraint("CK_NonTillCollections_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_NonTillCollections_Users_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ApprovedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.CheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CashMovements_Type", "\"Type\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_CashMovements_CashShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashShiftCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    DenominationId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShiftCounts", x => x.Id);
                    table.CheckConstraint("CK_CashShiftCounts_Phase", "\"Phase\" IN (1, 2)");
                    table.CheckConstraint("CK_CashShiftCounts_Quantity", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_CashShiftCounts_CashShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashShiftCounts_Denominations_DenominationId",
                        column: x => x.DenominationId,
                        principalTable: "Denominations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CashShiftId",
                table: "Payments",
                column: "CashShiftId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Backups_Trigger",
                table: "Backups",
                sql: "\"Trigger\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDayCloses_ClosedBy",
                table: "BusinessDayCloses",
                column: "ClosedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessDayCloses_ReopenedBy",
                table: "BusinessDayCloses",
                column: "ReopenedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ShiftId",
                table: "CashMovements",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShiftCounts_DenominationId",
                table: "CashShiftCounts",
                column: "DenominationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShiftCounts_ShiftId_Phase_DenominationId",
                table: "CashShiftCounts",
                columns: new[] { "ShiftId", "Phase", "DenominationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_BusinessDay",
                table: "CashShifts",
                column: "BusinessDay");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_CashierUserId_Open",
                table: "CashShifts",
                column: "CashierUserId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_DeviceId_Open",
                table: "CashShifts",
                column: "DeviceId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_HandedOverToShiftId",
                table: "CashShifts",
                column: "HandedOverToShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Denominations_Value_Kind",
                table: "Denominations",
                columns: new[] { "Value", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonTillCollections_BusinessDay",
                table: "NonTillCollections",
                column: "BusinessDay");

            migrationBuilder.CreateIndex(
                name: "IX_NonTillCollections_RecordedBy",
                table: "NonTillCollections",
                column: "RecordedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CashShifts_CashShiftId",
                table: "Payments",
                column: "CashShiftId",
                principalTable: "CashShifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing databases keep their roles, and DbSeeder only grants defaults to roles it creates, so the new
            // permissions are granted here (the Phase 13 defaults, frozen on purpose). ON CONFLICT keeps reruns safe.
            migrationBuilder.Sql("""
                INSERT INTO "RolePermissions" ("RoleId", "Permission")
                SELECT r."Id", p.permission
                FROM "Roles" r
                JOIN (VALUES
                    ('Admin', 'shift.operate'), ('Admin', 'shift.manage'), ('Admin', 'dayend.run'),
                    ('Manager', 'shift.operate'), ('Manager', 'shift.manage'), ('Manager', 'dayend.run'),
                    ('Cashier', 'shift.operate')
                ) AS p(role, permission) ON p.role = r."Name"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "Permission" IN ('shift.operate', 'shift.manage', 'dayend.run');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CashShifts_CashShiftId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "BusinessDayCloses");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "CashShiftCounts");

            migrationBuilder.DropTable(
                name: "NonTillCollections");

            migrationBuilder.DropTable(
                name: "CashShifts");

            migrationBuilder.DropTable(
                name: "Denominations");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CashShiftId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Backups_Trigger",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "CashShiftId",
                table: "Payments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Backups_Trigger",
                table: "Backups",
                sql: "\"Trigger\" IN (1, 2)");
        }
    }
}
