namespace Timesheet.Domain.Policies;

public sealed record BudgetState(decimal? RawPercent, decimal? DisplayPercent, bool IsAtRisk, bool IsOverspent);
public static class BudgetPolicy
{
    public static BudgetState Evaluate(decimal amount, decimal budget)
    {
        if (budget == 0) return new(null, null, false, false);
        var raw = amount / budget * 100m;
        return new(raw, Money.Round(raw), raw > 80m && raw <= 100m, raw > 100m);
    }
}
