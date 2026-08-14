using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelojChecador.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeHourlyRate",
                table: "Employees",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeeklySalary",
                table: "Employees",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeHourlyRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WeeklySalary",
                table: "Employees");
        }
    }
}
