using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodayDbGate.Infrastructure.Audit.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokenId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Input = table.Column<string>(type: "TEXT", nullable: true),
                    InputTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    RowCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: true),
                    RecordsAffected = table.Column<int>(type: "INTEGER", nullable: true),
                    Output = table.Column<byte[]>(type: "BLOB", nullable: true),
                    OutputBytes = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputTruncated = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Alias",
                table: "AuditLog",
                column: "Alias");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Timestamp",
                table: "AuditLog",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TokenName",
                table: "AuditLog",
                column: "TokenName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");
        }
    }
}
