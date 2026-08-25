using Timesheet.Domain.Errors;
namespace Timesheet.Domain.Policies;
public static class WorkHoursPolicy
{
    public const decimal DailyLimit = 24m;
    public const decimal OvertimeThreshold = 12m;
    public static void EnsureEntryHours(decimal hours)
    {
        if (hours <= 0 || hours > DailyLimit || decimal.Remainder(hours, 0.5m) != 0)
            throw new DomainException("VALIDATION_FAILED", "Часы должны быть положительными, кратными 0,5 и не больше 24.", details: new Dictionary<string, object?> { ["field"] = "hours" });
    }
    public static void EnsureDailyLimit(decimal alreadyLogged, decimal requested)
    {
        if (alreadyLogged + requested > DailyLimit)
            throw new DomainException("DAILY_HOURS_EXCEEDED", $"Суммарно за день получится {alreadyLogged + requested:0.#} ч, максимум — 24.", 409, new Dictionary<string, object?> { ["alreadyLogged"] = alreadyLogged, ["requested"] = requested, ["limit"] = DailyLimit });
    }
    public static bool IsOvertime(decimal dailyHours) => dailyHours > OvertimeThreshold;
}
