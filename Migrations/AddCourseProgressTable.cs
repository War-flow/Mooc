using Microsoft.EntityFrameworkCore.Migrations;

public partial class AddCourseProgressTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CourseProgresses",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CoursId = table.Column<int>(type: "int", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                LastAccessedBlock = table.Column<int>(type: "int", nullable: false),
                CompletedBlocks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                BlockInteractions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                LastAccessed = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsCompleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseProgresses", x => x.Id);
                table.ForeignKey(
                    name: "FK_CourseProgresses_Courses_CoursId",
                    column: x => x.CoursId,
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CourseProgresses_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_CourseProgresses_CoursId",
            table: "CourseProgresses",
            column: "CoursId");

        migrationBuilder.CreateIndex(
            name: "IX_CourseProgresses_UserId",
            table: "CourseProgresses",
            column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CourseProgresses");
    }
}