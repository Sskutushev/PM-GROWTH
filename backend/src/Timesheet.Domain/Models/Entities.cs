namespace Timesheet.Domain.Models;

/// <summary>A rate is in force from <paramref name="ValidFrom"/> until the next one starts.</summary>
public sealed record HourlyRate(DateOnly ValidFrom, decimal Value);

/// <summary>
/// Rate history lives inside the employee: it is small, always read in full and only
/// changes together with the employee. A separate collection would add a join for nothing.
/// </summary>
public sealed class Employee
{
    public required string Id { get; init; }

    public required string FullName { get; init; }

    public required string Department { get; init; }

    /// <summary>Order is not guaranteed; <c>RateResolver</c> sorts it.</summary>
    public List<HourlyRate> Rates { get; init; } = [];
}

public sealed class Project
{
    public required string Id { get; init; }

    /// <summary>Code such as "П-001". Uniqueness is enforced by a Mongo index.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public decimal Budget { get; init; }

    public DateOnly StartDate { get; init; }

    /// <summary><c>null</c> means the project has no end date.</summary>
    public DateOnly? EndDate { get; init; }
}

public sealed class TimeEntry
{
    public required string Id { get; init; }

    public required string EmployeeId { get; set; }

    public required string ProjectId { get; set; }

    /// <summary>Business date without a time part: a timesheet day must not depend on the server timezone.</summary>
    public DateOnly Date { get; set; }

    public decimal Hours { get; set; }

    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// The rate used at calculation time. Denormalised so the monthly report stays a plain
    /// <c>$match + $group</c> with no join into rate history; resynchronised when history changes.
    /// </summary>
    public decimal AppliedRate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Optimistic concurrency token: incremented on every successful write.</summary>
    public long Version { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record ClosedPeriod(int Year, int Month);
