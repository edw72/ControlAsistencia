using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlAsistencia.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryAndIdentificationToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Identification",
                table: "Employees",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Identification",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "Employees");
        }
    }
}
