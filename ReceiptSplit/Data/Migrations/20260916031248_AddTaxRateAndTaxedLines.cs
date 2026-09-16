using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptSplit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxRateAndTaxedLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored as thousandths of a percent; existing receipts predate the field, so assume Ontario's 13%.
            migrationBuilder.AddColumn<long>(
                name: "TaxRatePercent",
                table: "Receipts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 13000L);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxed",
                table: "ReceiptLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Both stores seen so far print a tax code only on taxed lines.
            migrationBuilder.Sql("UPDATE ReceiptLines SET IsTaxed = 1 WHERE TRIM(COALESCE(TaxCode, '')) <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "IsTaxed",
                table: "ReceiptLines");
        }
    }
}
