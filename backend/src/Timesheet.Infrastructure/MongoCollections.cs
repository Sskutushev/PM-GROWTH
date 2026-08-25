namespace Timesheet.Infrastructure;

/// <summary>
/// Имена коллекций в одном месте. Строковый литерал, разбросанный по репозиторию,
/// ловится только в runtime — и только на той ветке кода, куда дошёл тест.
/// </summary>
internal static class MongoCollections
{
    internal const string TimeEntries = "time_entries";
    internal const string Employees = "employees";
    internal const string Projects = "projects";
    internal const string ClosedPeriods = "closed_periods";

    internal static IReadOnlyList<string> All { get; } = [TimeEntries, Employees, Projects, ClosedPeriods];
}
