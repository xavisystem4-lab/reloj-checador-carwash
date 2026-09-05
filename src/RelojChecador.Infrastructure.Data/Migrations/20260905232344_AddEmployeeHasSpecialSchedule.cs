using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelojChecador.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeHasSpecialSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasSpecialSchedule",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasSpecialSchedule",
                table: "Employees");
        }
    }
}
