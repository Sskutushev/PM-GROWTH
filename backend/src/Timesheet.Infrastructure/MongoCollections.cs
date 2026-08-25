namespace Timesheet.Infrastructure;

/// <summary>
/// Collection names in one place. A string literal scattered across the repository only
/// fails at runtime, and only on the code path a test happened to reach.
/// </summary>
internal static class MongoCollections
{
    internal const string TimeEntries = "time_entries";
    internal const string Employees = "employees";
    internal const string Projects = "projects";
    internal const string ClosedPeriods = "closed_periods";

    internal static IReadOnlyList<string> All { get; } = [TimeEntries, Employees, Projects, ClosedPeriods];
}
