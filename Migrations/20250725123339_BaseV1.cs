using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class BaseV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseProgresses_AspNetUsers_UserId",
                table: "CourseProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseProgresses_Cours_CoursId",
                table: "CourseProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_Session_AspNetUsers_UserId",
                table: "Session");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CourseProgresses",
                table: "CourseProgresses");

            migrationBuilder.DropIndex(
                name: "IX_CourseProgresses_CoursId",
                table: "CourseProgresses");

            migrationBuilder.RenameTable(
                name: "CourseProgresses",
                newName: "CourseProgress");

            migrationBuilder.RenameIndex(
                name: "IX_CourseProgresses_UserId",
                table: "CourseProgress",
                newName: "IX_CourseProgress_UserId");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Session",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationSent1h",
                table: "Session",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationSent24h",
                table: "Session",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "CourseProgress",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CourseProgress",
                table: "CourseProgress",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "SessionEnrollments",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEnrollments", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SessionEnrollments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionEnrollments_Session_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Session",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseProgress_CoursId_UserId",
                table: "CourseProgress",
                columns: new[] { "CoursId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionEnrollments_SessionId",
                table: "SessionEnrollments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEnrollments_UserId",
                table: "SessionEnrollments",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseProgress_AspNetUsers_UserId",
                table: "CourseProgress",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseProgress_Cours_CoursId",
                table: "CourseProgress",
                column: "CoursId",
                principalTable: "Cours",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Session_AspNetUsers_UserId",
                table: "Session",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseProgress_AspNetUsers_UserId",
                table: "CourseProgress");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseProgress_Cours_CoursId",
                table: "CourseProgress");

            migrationBuilder.DropForeignKey(
                name: "FK_Session_AspNetUsers_UserId",
                table: "Session");

            migrationBuilder.DropTable(
                name: "SessionEnrollments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CourseProgress",
                table: "CourseProgress");

            migrationBuilder.DropIndex(
                name: "IX_CourseProgress_CoursId_UserId",
                table: "CourseProgress");

            migrationBuilder.DropColumn(
                name: "NotificationSent1h",
                table: "Session");

            migrationBuilder.DropColumn(
                name: "NotificationSent24h",
                table: "Session");

            migrationBuilder.RenameTable(
                name: "CourseProgress",
                newName: "CourseProgresses");

            migrationBuilder.RenameIndex(
                name: "IX_CourseProgress_UserId",
                table: "CourseProgresses",
                newName: "IX_CourseProgresses_UserId");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Session",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "CourseProgresses",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CourseProgresses",
                table: "CourseProgresses",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CourseProgresses_CoursId",
                table: "CourseProgresses",
                column: "CoursId");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseProgresses_AspNetUsers_UserId",
                table: "CourseProgresses",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseProgresses_Cours_CoursId",
                table: "CourseProgresses",
                column: "CoursId",
                principalTable: "Cours",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Session_AspNetUsers_UserId",
                table: "Session",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
