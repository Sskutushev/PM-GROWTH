using System.Globalization;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;

namespace Timesheet.Domain.Policies;

/// <summary>Everything needed to check the business rules of one entry.</summary>
public sealed record TimeEntryContext(
    Employee Employee,
    Project Project,
    DateOnly Date,
    decimal Hours,
    bool SourcePeriodClosed,
    bool TargetPeriodClosed,
    decimal HoursAlreadyLoggedThatDay);

/// <summary>
/// Check order is part of the contract: in a closed period the user must be told the period
/// is closed, not that hours must be a multiple of 0.5. The order is pinned by a test.
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

    /// <summary>Both bounds are inclusive; a missing end date means an open-ended project.</summary>
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
