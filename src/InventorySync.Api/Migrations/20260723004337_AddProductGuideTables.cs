using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventorySync.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductGuideTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductGuides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShopifyProductId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGuides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductGuideChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductGuideId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Embedding = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGuideChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductGuideChunks_ProductGuides_ProductGuideId",
                        column: x => x.ProductGuideId,
                        principalTable: "ProductGuides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductGuideChunks_ProductGuideId",
                table: "ProductGuideChunks",
                column: "ProductGuideId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductGuides_ShopifyProductId",
                table: "ProductGuides",
                column: "ShopifyProductId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductGuideChunks");

            migrationBuilder.DropTable(
                name: "ProductGuides");
        }
    }
}
