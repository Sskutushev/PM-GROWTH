namespace Timesheet.Domain.Policies;

/// <summary>
/// The single place where money is rounded. Halves go away from zero, not to even,
/// which is what <c>Math.Round(value, 2)</c> does by default.
/// </summary>
public static class Money
{
    public const int Scale = 2;

    public static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Round every entry, then sum the rounded values: otherwise the report total
    /// will not match the sum of the rows the user can see.
    /// </summary>
    public static decimal Calculate(decimal hours, decimal rate) => Round(hours * rate);
}
