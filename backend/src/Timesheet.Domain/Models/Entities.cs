namespace Timesheet.Domain.Models;

public sealed record HourlyRate(DateOnly ValidFrom, decimal Value);
public sealed class Employee { public required string Id { get; init; } public required string FullName { get; init; } public required string Department { get; init; } public List<HourlyRate> Rates { get; init; } = []; }
public sealed class Project { public required string Id { get; init; } public required string Code { get; init; } public required string Name { get; init; } public decimal Budget { get; init; } public DateOnly StartDate { get; init; } public DateOnly? EndDate { get; init; } }
public sealed class TimeEntry
{
    public required string Id { get; init; }
    public required string EmployeeId { get; set; }
    public required string ProjectId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string Comment { get; set; } = string.Empty;
    public decimal AppliedRate { get; set; }
    public decimal Amount { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed record ClosedPeriod(int Year, int Month);
