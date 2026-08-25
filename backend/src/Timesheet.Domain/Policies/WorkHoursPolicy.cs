using Timesheet.Domain.Errors;

namespace Timesheet.Domain.Policies;

/// <summary>Правила по часам: формат одной записи, суточный лимит и признак переработки.</summary>
public static class WorkHoursPolicy
{
    /// <summary>Жёсткий суточный лимит: больше 24 часов в календарных сутках не бывает.</summary>
    public const decimal DailyLimit = 24m;

    /// <summary>Мягкий порог: день сверх него сохраняется, но помечается как переработка.</summary>
    public const decimal OvertimeThreshold = 12m;

    /// <summary>Часы задаются с шагом в полчаса.</summary>
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
    /// Лимит считается по всем проектам сотрудника за дату.
    /// <paramref name="alreadyLogged"/> не включает изменяемую запись.
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
    /// Свойство дня, а не записи: появляется и исчезает при изменении соседних записей,
    /// поэтому вычисляется на чтении, а не хранится.
    /// </summary>
    public static bool IsOvertime(decimal dailyHours) => dailyHours > OvertimeThreshold;
}
