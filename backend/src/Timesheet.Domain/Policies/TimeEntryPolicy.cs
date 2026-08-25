using System.Globalization;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;

namespace Timesheet.Domain.Policies;

/// <summary>Данные, которых достаточно для проверки бизнес-правил записи.</summary>
public sealed record TimeEntryContext(
    Employee Employee,
    Project Project,
    DateOnly Date,
    decimal Hours,
    bool SourcePeriodClosed,
    bool TargetPeriodClosed,
    decimal HoursAlreadyLoggedThatDay);

/// <summary>
/// Порядок проверок — часть контракта: в закрытом периоде пользователь должен получить
/// «период закрыт», а не «часы кратны 0,5». Порядок закреплён тестом.
/// </summary>
public static class TimeEntryPolicy
{
    public static decimal ValidateAndResolveRate(TimeEntryContext context)
    {
        EnsurePeriodsAreOpen(context.SourcePeriodClosed, context.TargetPeriodClosed);

        WorkHoursPolicy.EnsureEntryHours(context.Hours);
        EnsureDateWithinProject(context.Project, context.Date);

        var rate = RateResolver.Resolve(context.Employee.Rates, context.Date);

        WorkHoursPolicy.EnsureDailyLimit(context.HoursAlreadyLoggedThatDay, context.Hours);

        return rate;
    }

    public static void EnsurePeriodsAreOpen(bool sourceClosed, bool targetClosed)
    {
        if (sourceClosed || targetClosed)
        {
            throw DomainException.Conflict(ErrorCodes.PeriodClosed, "Закрытый период нельзя изменять.");
        }
    }

    /// <summary>Границы включительны с обеих сторон; отсутствующее окончание — бессрочный проект.</summary>
    public static void EnsureDateWithinProject(Project project, DateOnly date)
    {
        var isBeforeStart = date < project.StartDate;
        var isAfterEnd = project.EndDate is { } end && date > end;

        if (!isBeforeStart && !isAfterEnd)
        {
            return;
        }

        var start = project.StartDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var finish = project.EndDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "без окончания";

        throw new DomainException(
            ErrorCodes.DateOutsideProjectPeriod,
            $"Дата должна входить в период проекта {start}–{finish}.",
            400,
            new Dictionary<string, object?>
            {
                ["field"] = "date",
                ["projectCode"] = project.Code,
                ["startDate"] = project.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = project.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
    }
}
