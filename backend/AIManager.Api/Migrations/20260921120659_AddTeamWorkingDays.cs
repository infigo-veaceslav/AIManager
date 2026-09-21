using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamWorkingDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingDays",
                table: "Teams",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "1,2,3,4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "Teams");
        }
    }
}
