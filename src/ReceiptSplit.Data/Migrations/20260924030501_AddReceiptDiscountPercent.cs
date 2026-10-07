using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptSplit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptDiscountPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiscountPercent",
                table: "Receipts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Receipts");
        }
    }
}
