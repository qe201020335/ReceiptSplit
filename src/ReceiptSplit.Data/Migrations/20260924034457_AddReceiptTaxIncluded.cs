using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptSplit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptTaxIncluded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TaxIncluded",
                table: "Receipts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxIncluded",
                table: "Receipts");
        }
    }
}
