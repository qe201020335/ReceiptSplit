namespace ReceiptSplit.Extraction;

/// <summary>
/// The prompt validated against real Costco and T&amp;T receipt photos on 2026-09-14, extended with store and date,
/// and with a storewide discount after a Target receipt printed "10%off Storewide" under its subtotal.
/// Rows are compact JSON arrays because output length dominates latency on the local model.
/// </summary>
internal static class ExtractionPrompt
{
    public const string System =
        "You read photos of shopping receipts. Extract every purchased line in printed order, including discount/coupon lines. " +
        "Skip section headers, payment, change, rounding, loyalty-point and other $0.00 info lines. " +
        "When a quantity or weight detail line (e.g. \"2 @ $0.78ea.\", \"0.615 Kg @ $5.49/Kg\") belongs to an item, " +
        "put that quantity in qty and do not output the detail line separately. " +
        "A percentage discount taken off the whole purchase after the subtotal (e.g. \"10%off Storewide\") is not a purchased line; put its percentage in d instead. " +
        "Negative amounts may be printed with a trailing minus (\"3.00-\") or in parentheses; output them with a leading minus.\n" +
        "Output format, nothing else:\n" +
        "- one line per purchased line, each a JSON array: [name,code,qty,amount,tax]\n" +
        "- then one final line: {\"s\":subtotal,\"d\":discount,\"t\":tax,\"T\":total,\"store\":store,\"date\":date}\n" +
        "name as printed; code is the item number as printed or null; qty is a number (1 if not printed); " +
        "amounts are strings without currency symbols; tax is the tax code letters printed next to the amount, or null; " +
        "discount is that percentage as a number (10 for 10% off), or null; " +
        "store is the store name printed at the top, or null; date is the purchase date as \"YYYY-MM-DD\", or null.";

    public const string User = "Extract this receipt.";
}
