using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelojChecador.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceUserPin = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifyMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    PunchType = table.Column<int>(type: "INTEGER", nullable: true),
                    RawPayload = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_BranchId",
                table: "Attendances",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_DeviceId_DeviceUserPin_TimestampUtc",
                table: "Attendances",
                columns: new[] { "DeviceId", "DeviceUserPin", "TimestampUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_EmployeeId",
                table: "Attendances",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attendances");
        }
    }
}
