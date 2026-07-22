using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventorySync.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGetLowStockProductsProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.GetLowStockProducts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ShopifyInventoryItemId, Title, Sku, Quantity, LowStockThreshold
    FROM dbo.Products
    WHERE Quantity <= LowStockThreshold;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.GetLowStockProducts");
        }
    }
}
