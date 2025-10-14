using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mooc.Migrations
{
    /// <inheritdoc />
    public partial class V9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchivedCoursTitle",
                table: "CourseBadges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedSessionTitle",
                table: "CourseBadges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedSessionEndDate",
                table: "Certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedSessionStartDate",
                table: "Certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedSessionTitle",
                table: "Certificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificate_CertificateNumber",
                table: "Certificates",
                column: "CertificateNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Certificate_CertificateNumber",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "ArchivedCoursTitle",
                table: "CourseBadges");

            migrationBuilder.DropColumn(
                name: "ArchivedSessionTitle",
                table: "CourseBadges");

            migrationBuilder.DropColumn(
                name: "ArchivedSessionEndDate",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "ArchivedSessionStartDate",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "ArchivedSessionTitle",
                table: "Certificates");
        }
    }
}
