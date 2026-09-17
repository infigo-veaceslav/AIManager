using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    JiraUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    JiraUser = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    JiraApiToken = table.Column<string>(type: "text", nullable: true),
                    TeamsPowerAutomateDmUrl = table.Column<string>(type: "text", nullable: true),
                    TeamsReportRecipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AnthropicApiKey = table.Column<string>(type: "text", nullable: true),
                    AnthropicBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AnthropicModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AnthropicMaxTokens = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
