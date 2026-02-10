using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkinAI.API.Migrations
{
    /// <inheritdoc />
    public partial class aa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserPushTokens_UserId_OneSignalPlayerId",
                table: "UserPushTokens",
                columns: new[] { "UserId", "OneSignalPlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPushTokens_UserId_OneSignalPlayerId",
                table: "UserPushTokens");
        }
    }
}
