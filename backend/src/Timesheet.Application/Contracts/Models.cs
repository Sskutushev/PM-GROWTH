namespace Timesheet.Application.Contracts;

public sealed record SaveTimeEntryRequest(string EmployeeId, string ProjectId, DateOnly Date, decimal Hours, string Comment, long? Version = null);
public sealed record TimeEntryView(string Id, string EmployeeId, string EmployeeName, string ProjectId, string ProjectCode, DateOnly Date, decimal Hours, decimal AppliedRate, decimal Amount, string Comment, bool IsOvertime, decimal DailyHours, long Version);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, decimal TotalHours, decimal TotalAmount);
public sealed record ProjectReportRow(string ProjectId, string ProjectCode, string ProjectName, decimal Hours, decimal Amount, decimal Budget, decimal? Percent, bool IsAtRisk, bool IsOverspent);
public sealed record ProjectReport(IReadOnlyList<ProjectReportRow> Items, decimal TotalHours, decimal TotalAmount);
public sealed record LookupItem(string Id, string Code, string Name);
public sealed record TimeEntryQuery(int Year, int Month, string? EmployeeId, string? ProjectId, int Page = 1, int PageSize = 25);
public sealed record RateUpdateRequest(IReadOnlyList<RateInput> Rates);
public sealed record RateInput(DateOnly ValidFrom, decimal Value);
public sealed record RecalculationResult(long Recalculated, long SkippedInClosedPeriods);
