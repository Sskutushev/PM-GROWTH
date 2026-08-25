using Timesheet.Domain.Models;
namespace Timesheet.Application.Contracts;
public interface ITimesheetStore
{
    Task<Employee?> GetEmployee(string id, CancellationToken cancellationToken);
    Task<Project?> GetProject(string id, CancellationToken cancellationToken);
    Task<TimeEntry?> GetEntry(string id, CancellationToken cancellationToken);
    Task<bool> IsPeriodClosed(DateOnly entryDate, CancellationToken cancellationToken);
    Task<decimal> GetDailyHours(string employeeId, DateOnly entryDate, string? excludingId, CancellationToken cancellationToken);
    Task<TimeEntry> Insert(TimeEntry entry, CancellationToken cancellationToken);
    Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken cancellationToken);
    Task<bool> Delete(string id, long? expectedVersion, CancellationToken cancellationToken);
    Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken cancellationToken);
    Task<ProjectReport> Report(int year, int month, CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> Employees(CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> Projects(CancellationToken cancellationToken);
    Task SetPeriod(int year, int month, bool closed, CancellationToken cancellationToken);
    Task Seed(CancellationToken cancellationToken);
    Task<RecalculationResult> UpdateRates(string employeeId, IReadOnlyList<HourlyRate> rates, CancellationToken cancellationToken);
}
