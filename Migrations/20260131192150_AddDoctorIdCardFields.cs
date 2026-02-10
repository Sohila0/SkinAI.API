using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkinAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorIdCardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IdCardUploadedAt",
                table: "Doctors",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdCardUploadedAt",
                table: "Doctors");
        }
    }
}
