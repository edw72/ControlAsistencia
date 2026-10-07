using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlAsistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedLunchToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasUnifiedLunch",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasUnifiedLunch",
                table: "Employees");
        }
    }
}
