using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class V8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Certificates_Session_SessionId",
                table: "Certificates");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseBadges_Cours_CoursId",
                table: "CourseBadges");

            migrationBuilder.AlterColumn<int>(
                name: "CoursId",
                table: "CourseBadges",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "SessionId",
                table: "Certificates",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_Certificates_Session_SessionId",
                table: "Certificates",
                column: "SessionId",
                principalTable: "Session",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseBadges_Cours_CoursId",
                table: "CourseBadges",
                column: "CoursId",
                principalTable: "Cours",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Certificates_Session_SessionId",
                table: "Certificates");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseBadges_Cours_CoursId",
                table: "CourseBadges");

            migrationBuilder.AlterColumn<int>(
                name: "CoursId",
                table: "CourseBadges",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SessionId",
                table: "Certificates",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Certificates_Session_SessionId",
                table: "Certificates",
                column: "SessionId",
                principalTable: "Session",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseBadges_Cours_CoursId",
                table: "CourseBadges",
                column: "CoursId",
                principalTable: "Cours",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
