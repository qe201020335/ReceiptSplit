namespace ReceiptSplit.Data;

/// <summary>
/// The precision the database keeps: money to the cent, tax rates to a thousandth of a percent. Values are rounded
/// with this before a receipt is checked, so its status agrees with the numbers that are saved and read back.
/// </summary>
public static class Precision
{
    public static decimal Cents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal? Cents(decimal? amount) => amount is { } value ? Cents(value) : null;

    public static decimal Rate(decimal percent) => Math.Round(percent, 3, MidpointRounding.AwayFromZero);
}
