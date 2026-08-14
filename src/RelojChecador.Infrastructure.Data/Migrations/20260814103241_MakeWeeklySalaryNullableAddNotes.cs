using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelojChecador.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeWeeklySalaryNullableAddNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "WeeklySalary",
                table: "Employees",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Employees",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Employees");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeeklySalary",
                table: "Employees",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
