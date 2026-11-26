using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduScroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoCatalogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhitelistedChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YouTubeChannelId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    TopicCategory = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TrustScore = table.Column<double>(type: "float", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhitelistedChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YouTubeVideoId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    WhitelistedChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChannelExternalId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ChannelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Language = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DiscoverySource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogVideos_WhitelistedChannels_WhitelistedChannelId",
                        column: x => x.WhitelistedChannelId,
                        principalTable: "WhitelistedChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogVideos_Language_PublishedAtUtc",
                table: "CatalogVideos",
                columns: new[] { "Language", "PublishedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogVideos_WhitelistedChannelId",
                table: "CatalogVideos",
                column: "WhitelistedChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogVideos_YouTubeVideoId",
                table: "CatalogVideos",
                column: "YouTubeVideoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhitelistedChannels_IsActive_Language",
                table: "WhitelistedChannels",
                columns: new[] { "IsActive", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_WhitelistedChannels_YouTubeChannelId",
                table: "WhitelistedChannels",
                column: "YouTubeChannelId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogVideos");

            migrationBuilder.DropTable(
                name: "WhitelistedChannels");
        }
    }
}
