using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduScroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiLanguageFeedQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "SourceSearchQueries",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "TargetFeedLanguage",
                table: "SourceAnalyses",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "En");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "SourceSearchQueries");

            migrationBuilder.DropColumn(
                name: "TargetFeedLanguage",
                table: "SourceAnalyses");
        }
    }
}
