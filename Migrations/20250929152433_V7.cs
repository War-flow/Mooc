using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class V7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CoursId = table.Column<int>(type: "integer", nullable: false),
                    BadgeType = table.Column<int>(type: "integer", nullable: false),
                    ScorePercentage = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    PointsEarned = table.Column<int>(type: "integer", nullable: false),
                    TotalPointsPossible = table.Column<int>(type: "integer", nullable: false),
                    CorrectAnswers = table.Column<int>(type: "integer", nullable: false),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: false),
                    EarnedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseBadges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseBadges_Cours_CoursId",
                        column: x => x.CoursId,
                        principalTable: "Cours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseBadge_User_Cours",
                table: "CourseBadges",
                columns: new[] { "UserId", "CoursId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseBadges_CoursId",
                table: "CourseBadges",
                column: "CoursId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseBadges");
        }
    }
}
