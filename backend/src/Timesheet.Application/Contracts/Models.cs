namespace Timesheet.Application.Contracts;

// ---------- Time entry ----------

/// <summary>
/// Request body for creating and editing an entry. <see cref="Version"/> is required on edit:
/// it is the version the client saw when the form was opened, and it is how concurrent
/// editing is detected.
/// </summary>
public sealed record SaveTimeEntryRequest(
    string EmployeeId,
    string ProjectId,
    DateOnly Date,
    decimal Hours,
    string Comment,
    long? Version = null);

/// <summary>A timesheet row shaped the way the UI displays it.</summary>
public sealed record TimeEntryView(
    string Id,
    string EmployeeId,
    string EmployeeName,
    string ProjectId,
    string ProjectCode,
    DateOnly Date,
    decimal Hours,
    decimal AppliedRate,
    decimal Amount,
    string Comment,
    bool IsOvertime,
    decimal DailyHours,
    long Version);

/// <summary>Pagination bounds. The upper limit stops a client from asking for a whole month in one page.</summary>
public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}

public sealed record TimeEntryQuery(
    int Year,
    int Month,
    string? EmployeeId,
    string? ProjectId,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// A page of results. Totals (<see cref="TotalHours"/>, <see cref="TotalAmount"/>) come from a
/// separate aggregation over the full filter, not from the rows on the page: otherwise a
/// paginated user would see one page's total presented as the month's total.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount,
    decimal TotalHours,
    decimal TotalAmount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}

// ---------- Report ----------

public sealed record ProjectReportRow(
    string ProjectId,
    string ProjectCode,
    string ProjectName,
    decimal Hours,
    decimal Amount,
    decimal Budget,
    decimal? Percent,
    bool IsAtRisk,
    bool IsOverspent);

public sealed record ProjectReport(
    IReadOnlyList<ProjectReportRow> Items,
    decimal TotalHours,
    decimal TotalAmount);

// ---------- Catalogues ----------

public sealed record LookupItem(string Id, string Code, string Name);

// ---------- Rates ----------

public sealed record RateUpdateRequest(IReadOnlyList<RateInput> Rates);

public sealed record RateInput(DateOnly ValidFrom, decimal Value);

public sealed record RecalculationResult(long Recalculated, long SkippedInClosedPeriods);

// ---------- Maintenance ----------

/// <summary>Indexes of one collection as they exist in the database right now.</summary>
public sealed record IndexReport(string Collection, IReadOnlyList<string> Indexes);

// ---------- Periods ----------

/// <summary>
/// Request body for closing and reopening a month. It lives in Application rather than
/// Program.cs because the contract has a validator and the two belong together.
/// </summary>
public sealed record PeriodRequest(int Year, int Month);
