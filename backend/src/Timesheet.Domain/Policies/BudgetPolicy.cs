namespace Timesheet.Domain.Policies;

/// <summary>
/// Флаги считаются по <paramref name="RawPercent"/>, в интерфейс идёт <paramref name="DisplayPercent"/>.
/// Разделение обязательно: 100,004 % округляется до 100,00 %, и перерасход пропал бы с экрана.
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
        // Процент не определён: null вместо Infinity/NaN, интерфейс покажет прочерк.
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
