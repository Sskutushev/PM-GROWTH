using Timesheet.Domain.Errors;

namespace Timesheet.Domain.Policies;

/// <summary>Hour rules: the shape of a single entry, the daily cap and the overtime flag.</summary>
public static class WorkHoursPolicy
{
    /// <summary>Hard daily cap: a calendar day cannot hold more than 24 hours.</summary>
    public const decimal DailyLimit = 24m;

    /// <summary>Soft threshold: a day above it is still saved, but flagged as overtime.</summary>
    public const decimal OvertimeThreshold = 12m;

    /// <summary>Hours are entered in half-hour steps.</summary>
    public const decimal Step = 0.5m;

    public static void EnsureEntryHours(decimal hours)
    {
        if (hours <= 0 || hours > DailyLimit || decimal.Remainder(hours, Step) != 0)
        {
            throw new DomainException(
                ErrorCodes.ValidationFailed,
                "Часы должны быть положительными, кратными 0,5 и не больше 24.",
                400,
                new Dictionary<string, object?>
                {
                    ["field"] = "hours",
                    ["step"] = Step,
                    ["max"] = DailyLimit,
                });
        }
    }

    /// <summary>
    /// The cap spans every project the employee logged that date.
    /// <paramref name="alreadyLogged"/> excludes the entry currently being edited.
    /// </summary>
    public static void EnsureDailyLimit(decimal alreadyLogged, decimal requested)
    {
        var total = alreadyLogged + requested;
        if (total <= DailyLimit)
        {
            return;
        }

        throw DomainException.Conflict(
            ErrorCodes.DailyHoursExceeded,
            $"Суммарно за день получится {total:0.#} ч, максимум — 24.",
            new Dictionary<string, object?>
            {
                ["alreadyLogged"] = alreadyLogged,
                ["requested"] = requested,
                ["limit"] = DailyLimit,
            });
    }

    /// <summary>
    /// A property of the day, not of the entry: it appears and disappears as neighbouring
    /// entries change, so it is computed on read instead of being stored.
    /// </summary>
    public static bool IsOvertime(decimal dailyHours) => dailyHours > OvertimeThreshold;
}
