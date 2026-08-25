using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
namespace Timesheet.Domain.Policies;

public static class TimeEntryPolicy
{
    public static decimal ValidateAndCalculate(bool oldPeriodClosed, bool newPeriodClosed, decimal hours, Project project, Employee employee, DateOnly date, decimal alreadyLogged)
    {
        if (oldPeriodClosed || newPeriodClosed) throw new DomainException("PERIOD_CLOSED", "Закрытый период нельзя изменять.", 409);
        WorkHoursPolicy.EnsureEntryHours(hours);
        if (date < project.StartDate || (project.EndDate is { } end && date > end)) throw new DomainException("DATE_OUTSIDE_PROJECT_PERIOD", $"Дата должна входить в период проекта {project.StartDate:dd.MM.yyyy}–{project.EndDate?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "без окончания"}.");
        var rate = RateResolver.Resolve(employee.Rates, date);
        WorkHoursPolicy.EnsureDailyLimit(alreadyLogged, hours);
        return rate;
    }
}
