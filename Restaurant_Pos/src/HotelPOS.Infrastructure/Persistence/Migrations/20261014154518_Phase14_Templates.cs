using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HotelPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14_Templates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LayoutJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ActivatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                    table.CheckConstraint("CK_DocumentTemplates_DocumentType", "\"DocumentType\" IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "PrintLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    CopyNumber = table.Column<int>(type: "integer", nullable: false),
                    Output = table.Column<int>(type: "integer", nullable: false),
                    PrinterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintLogs", x => x.Id);
                    table.CheckConstraint("CK_PrintLogs_CopyNumber", "\"CopyNumber\" >= 1");
                    table.CheckConstraint("CK_PrintLogs_DocumentType", "\"DocumentType\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_PrintLogs_Output", "\"Output\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_PrintLogs_Result", "\"Result\" IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_ActivePerType",
                table: "DocumentTemplates",
                column: "DocumentType",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_DocumentType_Version",
                table: "DocumentTemplates",
                columns: new[] { "DocumentType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintLogs_CreatedAt",
                table: "PrintLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PrintLogs_DocumentType_DocumentId_CopyNumber",
                table: "PrintLogs",
                columns: new[] { "DocumentType", "DocumentId", "CopyNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTemplates");

            migrationBuilder.DropTable(
                name: "PrintLogs");
        }
    }
}
