using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkinAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorVerificationFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationFileName",
                table: "Doctors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationFileUrl",
                table: "Doctors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationUploadedAt",
                table: "Doctors",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationFileName",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "VerificationFileUrl",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "VerificationUploadedAt",
                table: "Doctors");
        }
    }
}
