using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduScroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceAnalysisTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ConceptsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AnalyzedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceAnalyses_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceSearchQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceSearchQueries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceSearchQueries_SourceAnalyses_SourceAnalysisId",
                        column: x => x.SourceAnalysisId,
                        principalTable: "SourceAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ParentTopicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceTopics_SourceAnalyses_SourceAnalysisId",
                        column: x => x.SourceAnalysisId,
                        principalTable: "SourceAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceAnalyses_OwnerUserId_SourceId",
                table: "SourceAnalyses",
                columns: new[] { "OwnerUserId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceAnalyses_SourceId",
                table: "SourceAnalyses",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceSearchQueries_SourceAnalysisId_DisplayOrder",
                table: "SourceSearchQueries",
                columns: new[] { "SourceAnalysisId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceTopics_SourceAnalysisId_DisplayOrder",
                table: "SourceTopics",
                columns: new[] { "SourceAnalysisId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceSearchQueries");

            migrationBuilder.DropTable(
                name: "SourceTopics");

            migrationBuilder.DropTable(
                name: "SourceAnalyses");
        }
    }
}
