using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RelojChecador.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncCursors",
                columns: table => new
                {
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CursorUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCursors", x => x.EntityType);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncCursors");
        }
    }
}
