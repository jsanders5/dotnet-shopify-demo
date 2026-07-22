using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventorySync.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyVariantIdToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ShopifyVariantId",
                table: "Products",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopifyVariantId",
                table: "Products");
        }
    }
}
