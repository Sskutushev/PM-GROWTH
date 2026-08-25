namespace Timesheet.Domain.Policies;

/// <summary>
/// Flags are derived from <paramref name="RawPercent"/>; the UI shows <paramref name="DisplayPercent"/>.
/// The split matters: 100.004% rounds to 100.00% and the overspend would vanish from the screen.
/// </summary>
public sealed record BudgetState(
    decimal? RawPercent,
    decimal? DisplayPercent,
    bool IsAtRisk,
    bool IsOverspent);

public static class BudgetPolicy
{
    public const decimal RiskThreshold = 80m;
    public const decimal OverspendThreshold = 100m;

    public static BudgetState Evaluate(decimal amount, decimal budget)
    {
        // The percentage is undefined: null rather than Infinity/NaN, and the UI shows a dash.
        if (budget == 0)
        {
            return new BudgetState(null, null, false, false);
        }

        var raw = amount / budget * 100m;

        return new BudgetState(
            RawPercent: raw,
            DisplayPercent: Money.Round(raw),
            IsAtRisk: raw > RiskThreshold && raw <= OverspendThreshold,
            IsOverspent: raw > OverspendThreshold);
    }
}
