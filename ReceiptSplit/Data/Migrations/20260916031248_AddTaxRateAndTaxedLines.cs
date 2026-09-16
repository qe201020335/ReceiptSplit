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

            // Backfill from the printed codes, using the same list as ReceiptTaxCodes so that exempt
            // markers such as Costco US's "E" are not read as taxed.
            migrationBuilder.Sql(
                """
                WITH taxed(code) AS (
                    VALUES ('A'),('G'),('H'),('P'),('Q'),('S'),('T'),('GST'),('HST'),('PST'),('QST'),('TPS'),('TVQ'))
                UPDATE ReceiptLines SET IsTaxed = 1
                WHERE EXISTS (
                    SELECT 1 FROM taxed
                    WHERE ' ' || UPPER(REPLACE(REPLACE(COALESCE(ReceiptLines.TaxCode, ''), ',', ' '), '/', ' ')) || ' '
                        LIKE '% ' || taxed.code || ' %');
                """);

            // Receipts extracted before the tax check ran keep a status that ignores it; re-check them here.
            // Amounts are cents and the rate is thousandths of a percent, matching ReceiptChecks' one-cent tolerance.
            migrationBuilder.Sql(
                """
                UPDATE Receipts SET Status = 'NeedsReview'
                WHERE Status = 'Completed'
                  AND ABS(COALESCE(Tax, 0) - CAST(ROUND(
                      (SELECT COALESCE(SUM(Amount), 0) FROM ReceiptLines l WHERE l.ReceiptId = Receipts.Id AND l.IsTaxed = 1)
                      * TaxRatePercent / 100000.0) AS INTEGER)) > 1;
                """);
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
