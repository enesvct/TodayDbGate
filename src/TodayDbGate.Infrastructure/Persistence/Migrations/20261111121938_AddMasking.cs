using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodayDbGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMasking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_see_unmasked",
                table: "Tokens",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "detect_personal_data",
                table: "Connections",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "MaskingRules",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    connection_id = table.Column<int>(type: "INTEGER", nullable: false),
                    schema_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    table_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    column_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaskingRules", x => x.id);
                    table.ForeignKey(
                        name: "FK_MaskingRules_Connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "Connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaskingRules_connection_id_schema_name_table_name_column_name",
                table: "MaskingRules",
                columns: new[] { "connection_id", "schema_name", "table_name", "column_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaskingRules");

            migrationBuilder.DropColumn(
                name: "can_see_unmasked",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "detect_personal_data",
                table: "Connections");
        }
    }
}
