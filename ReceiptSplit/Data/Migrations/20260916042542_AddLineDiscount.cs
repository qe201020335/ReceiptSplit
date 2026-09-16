using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReceiptSplit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Discount",
                table: "ReceiptLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Fold promotions already extracted into the item each one names, the way ReceiptPromotions does
            // for new receipts: "TPD/<item number>" or "/<item number>" on a negative line, matched to the
            // closest line above it with that item number. Amounts are cents.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE PromotionsToBake AS
                SELECT promo.Id AS PromoId, promo.Amount AS PromoAmount, (
                    SELECT item.Id FROM ReceiptLines AS item
                    WHERE item.ReceiptId = promo.ReceiptId
                      AND item.Position < promo.Position
                      AND item.Amount >= 0
                      AND item.Code = TRIM(SUBSTR(promo.Name, INSTR(promo.Name, '/') + 1))
                    ORDER BY item.Position DESC LIMIT 1) AS ItemId
                FROM ReceiptLines AS promo
                WHERE promo.Amount < 0
                  AND INSTR(promo.Name, '/') > 0
                  AND UPPER(TRIM(SUBSTR(TRIM(promo.Name), 1, INSTR(TRIM(promo.Name), '/') - 1))) IN ('', 'TPD')
                  AND TRIM(SUBSTR(promo.Name, INSTR(promo.Name, '/') + 1)) <> ''
                  AND TRIM(SUBSTR(promo.Name, INSTR(promo.Name, '/') + 1)) NOT GLOB '*[^0-9]*';
                """);

            migrationBuilder.Sql(
                """
                UPDATE ReceiptLines SET
                    Amount = Amount + (SELECT SUM(PromoAmount) FROM PromotionsToBake WHERE ItemId = ReceiptLines.Id),
                    Discount = Discount + (SELECT SUM(PromoAmount) FROM PromotionsToBake WHERE ItemId = ReceiptLines.Id)
                WHERE Id IN (SELECT ItemId FROM PromotionsToBake WHERE ItemId IS NOT NULL);
                """);

            migrationBuilder.Sql(
                "DELETE FROM ReceiptLines WHERE Id IN (SELECT PromoId FROM PromotionsToBake WHERE ItemId IS NOT NULL);");

            // Positions are left contiguous so the lines still read 1, 2, 3 in printed order.
            migrationBuilder.Sql(
                """
                UPDATE ReceiptLines SET Position = (
                    SELECT COUNT(*) FROM ReceiptLines AS earlier
                    WHERE earlier.ReceiptId = ReceiptLines.ReceiptId AND earlier.Position < ReceiptLines.Position)
                WHERE ReceiptId IN (
                    SELECT DISTINCT ReceiptId FROM ReceiptLines AS line
                    WHERE line.Id IN (SELECT ItemId FROM PromotionsToBake WHERE ItemId IS NOT NULL));
                """);

            migrationBuilder.Sql("DROP TABLE PromotionsToBake;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Discount",
                table: "ReceiptLines");
        }
    }
}
