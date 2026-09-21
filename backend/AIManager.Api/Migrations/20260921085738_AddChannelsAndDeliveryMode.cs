using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AIManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelsAndDeliveryMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryMode",
                table: "ChaseRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DestinationChannelId",
                table: "ChaseRules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamsChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeamId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsChannels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChaseRules_DestinationChannelId",
                table: "ChaseRules",
                column: "DestinationChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChaseRules_TeamsChannels_DestinationChannelId",
                table: "ChaseRules",
                column: "DestinationChannelId",
                principalTable: "TeamsChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChaseRules_TeamsChannels_DestinationChannelId",
                table: "ChaseRules");

            migrationBuilder.DropTable(
                name: "TeamsChannels");

            migrationBuilder.DropIndex(
                name: "IX_ChaseRules_DestinationChannelId",
                table: "ChaseRules");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "ChaseRules");

            migrationBuilder.DropColumn(
                name: "DestinationChannelId",
                table: "ChaseRules");
        }
    }
}
