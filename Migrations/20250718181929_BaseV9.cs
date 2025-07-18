using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class BaseV9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "EndDate",
                table: "Session",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "EndDate",
                table: "Session",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "date");
        }
    }
}
