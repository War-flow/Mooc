using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class AddShowInTrombinoscopeToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowInTrombinoscope",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowInTrombinoscope",
                table: "AspNetUsers");
        }
    }
}