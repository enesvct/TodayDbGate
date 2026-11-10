using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodayDbGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    alias_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    database_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    connection_string_encrypted = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    token_prefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "TokenConnections",
                columns: table => new
                {
                    token_id = table.Column<int>(type: "INTEGER", nullable: false),
                    connection_id = table.Column<int>(type: "INTEGER", nullable: false),
                    access_level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenConnections", x => new { x.token_id, x.connection_id });
                    table.ForeignKey(
                        name: "FK_TokenConnections_Connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "Connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TokenConnections_Tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "Tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_alias_name",
                table: "Connections",
                column: "alias_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenConnections_connection_id",
                table: "TokenConnections",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_token_hash",
                table: "Tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenConnections");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropTable(
                name: "Tokens");
        }
    }
}
