using Timesheet.Domain.Models;

namespace Timesheet.Application.Contracts;

/// <summary>
/// Storage port. Application knows nothing about Mongo or BSON: scenarios run against an
/// in-memory implementation, and the reporting side can move to another store (see SCALING.md).
/// </summary>
public interface ITimesheetStore
{
    // ---------- Catalogues ----------

    Task<Employee?> GetEmployee(string id, CancellationToken cancellationToken);

    Task<Project?> GetProject(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> Employees(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> Projects(CancellationToken cancellationToken);

    // ---------- Periods ----------

    Task<bool> IsPeriodClosed(DateOnly entryDate, CancellationToken cancellationToken);

    Task SetPeriod(int year, int month, bool closed, CancellationToken cancellationToken);

    // ---------- Time entries ----------

    Task<TimeEntry?> GetEntry(string id, CancellationToken cancellationToken);

    /// <summary>Hours logged by an employee on a date across all projects, excluding the entry being edited.</summary>
    Task<decimal> GetDailyHours(
        string employeeId,
        DateOnly entryDate,
        string? excludingId,
        CancellationToken cancellationToken);

    Task<TimeEntry> Insert(TimeEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Replace matched on <c>(_id, expectedVersion)</c>. <c>null</c> means the version did not
    /// match — a concurrent edit, not a missing document.
    /// </summary>
    Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Delete matched on <c>(_id, expectedVersion)</c>. The version is required: a delete that
    /// skipped it would be the one write able to walk past optimistic concurrency.
    /// </summary>
    Task<bool> Delete(string id, long expectedVersion, CancellationToken cancellationToken);

    Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken cancellationToken);

    // ---------- Report ----------

    /// <summary>Grouping happens in the database: one row per project comes back, not the entries.</summary>
    Task<ProjectReport> Report(int year, int month, CancellationToken cancellationToken);

    // ---------- Rates ----------

    Task<RecalculationResult> UpdateRates(
        string employeeId,
        IReadOnlyList<HourlyRate> rates,
        CancellationToken cancellationToken);

    // ---------- Maintenance ----------

    Task Seed(CancellationToken cancellationToken);

    /// <summary>Creates the missing indexes. Idempotent; called at startup and after seeding.</summary>
    Task EnsureIndexes(CancellationToken cancellationToken);

    /// <summary>The indexes that actually exist, for diagnostics and for the test that guards them.</summary>
    Task<IReadOnlyList<IndexReport>> DescribeIndexes(CancellationToken cancellationToken);
}
