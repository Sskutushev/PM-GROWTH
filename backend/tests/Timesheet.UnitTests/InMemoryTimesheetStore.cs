using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;

namespace Timesheet.UnitTests;

// In-memory порт хранилища: сценарии прикладного слоя (создание, изменение, конкурентность,
// перенос между периодами) проверяются без контейнера с Mongo. Интеграционные тесты поверх
// настоящей базы проверяют другое — что запросы и индексы работают.
internal sealed class InMemoryTimesheetStore : ITimesheetStore
{
    private readonly Dictionary<string, Employee> employees = [];
    private readonly Dictionary<string, Project> projects = [];
    private readonly Dictionary<string, TimeEntry> entries = [];
    private readonly HashSet<(int Year, int Month)> closedPeriods = [];

    /// <summary>Счётчик обращений: используется тестом, который следит за отсутствием N+1.</summary>
    internal int DailyHoursCalls { get; private set; }

    internal InMemoryTimesheetStore WithEmployee(Employee employee)
    {
        employees[employee.Id] = employee;
        return this;
    }

    internal InMemoryTimesheetStore WithProject(Project project)
    {
        projects[project.Id] = project;
        return this;
    }

    internal InMemoryTimesheetStore WithClosedPeriod(int year, int month)
    {
        closedPeriods.Add((year, month));
        return this;
    }

    internal InMemoryTimesheetStore WithEntry(TimeEntry entry)
    {
        entries[entry.Id] = entry;
        return this;
    }

    internal TimeEntry Stored(string id) => entries[id];

    internal int EntryCount => entries.Count;

    /// <summary>Имитирует чужое сохранение между чтением и записью: версия уходит вперёд.</summary>
    internal void BumpVersionOutOfBand(string id)
    {
        var entry = entries[id];
        entry.Version++;
        entry.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task<Employee?> GetEmployee(string id, CancellationToken ct) =>
        Task.FromResult(employees.GetValueOrDefault(id));

    public Task<Project?> GetProject(string id, CancellationToken ct) =>
        Task.FromResult(projects.GetValueOrDefault(id));

    public Task<IReadOnlyList<LookupItem>> Employees(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LookupItem>>(employees.Values
            .OrderBy(x => x.FullName, StringComparer.Ordinal)
            .Select(x => new LookupItem(x.Id, string.Empty, x.FullName))
            .ToArray());

    public Task<IReadOnlyList<LookupItem>> Projects(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LookupItem>>(projects.Values
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new LookupItem(x.Id, x.Code, x.Name))
            .ToArray());

    public Task<bool> IsPeriodClosed(DateOnly date, CancellationToken ct) =>
        Task.FromResult(closedPeriods.Contains((date.Year, date.Month)));

    public Task SetPeriod(int year, int month, bool closed, CancellationToken ct)
    {
        if (closed)
        {
            closedPeriods.Add((year, month));
        }
        else
        {
            closedPeriods.Remove((year, month));
        }

        return Task.CompletedTask;
    }

    public Task<TimeEntry?> GetEntry(string id, CancellationToken ct)
    {
        // Копия: до Replace сервис не должен править то, что лежит в «базе».
        var entry = entries.GetValueOrDefault(id);
        return Task.FromResult(entry is null ? null : Clone(entry));
    }

    public Task<decimal> GetDailyHours(string employeeId, DateOnly date, string? excludingId, CancellationToken ct)
    {
        DailyHoursCalls++;

        var total = entries.Values
            .Where(x => x.EmployeeId == employeeId && x.Date == date && x.Id != excludingId)
            .Sum(x => x.Hours);

        return Task.FromResult(total);
    }

    public Task<TimeEntry> Insert(TimeEntry entry, CancellationToken ct)
    {
        entries[entry.Id] = Clone(entry);
        return Task.FromResult(entry);
    }

    public Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken ct)
    {
        if (!entries.TryGetValue(entry.Id, out var stored) || stored.Version != expectedVersion)
        {
            return Task.FromResult<TimeEntry?>(null);
        }

        entries[entry.Id] = Clone(entry);
        return Task.FromResult<TimeEntry?>(entry);
    }

    public Task<bool> Delete(string id, long? expectedVersion, CancellationToken ct)
    {
        if (!entries.TryGetValue(id, out var stored))
        {
            return Task.FromResult(false);
        }

        if (expectedVersion is not null && stored.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(entries.Remove(id));
    }

    public Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken ct)
    {
        var range = MonthRange.Create(query.Year, query.Month);

        var filtered = entries.Values
            .Where(x => range.Contains(x.Date))
            .Where(x => string.IsNullOrWhiteSpace(query.EmployeeId) || x.EmployeeId == query.EmployeeId)
            .Where(x => string.IsNullOrWhiteSpace(query.ProjectId) || x.ProjectId == query.ProjectId)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        var page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(entry =>
            {
                var dailyHours = entries.Values
                    .Where(x => x.EmployeeId == entry.EmployeeId && x.Date == entry.Date)
                    .Sum(x => x.Hours);

                return new TimeEntryView(
                    entry.Id,
                    entry.EmployeeId,
                    employees.TryGetValue(entry.EmployeeId, out var employee) ? employee.FullName : "Удалённый сотрудник",
                    entry.ProjectId,
                    projects.TryGetValue(entry.ProjectId, out var project) ? project.Code : "Удалённый проект",
                    entry.Date,
                    entry.Hours,
                    entry.AppliedRate,
                    entry.Amount,
                    entry.Comment,
                    WorkHoursPolicy.IsOvertime(dailyHours),
                    dailyHours,
                    entry.Version);
            })
            .ToArray();

        return Task.FromResult(new PagedResult<TimeEntryView>(
            page,
            query.Page,
            query.PageSize,
            filtered.Length,
            filtered.Sum(x => x.Hours),
            filtered.Sum(x => x.Amount)));
    }

    public Task<ProjectReport> Report(int year, int month, CancellationToken ct)
    {
        var range = MonthRange.Create(year, month);

        var rows = entries.Values
            .Where(x => range.Contains(x.Date))
            .GroupBy(x => x.ProjectId)
            .Where(g => projects.ContainsKey(g.Key))
            .Select(g =>
            {
                var project = projects[g.Key];
                var hours = g.Sum(x => x.Hours);
                var amount = g.Sum(x => x.Amount);
                var budget = BudgetPolicy.Evaluate(amount, project.Budget);

                return new ProjectReportRow(
                    project.Id,
                    project.Code,
                    project.Name,
                    hours,
                    amount,
                    project.Budget,
                    budget.DisplayPercent,
                    budget.IsAtRisk,
                    budget.IsOverspent);
            })
            .OrderBy(x => x.ProjectCode, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new ProjectReport(rows, rows.Sum(x => x.Hours), rows.Sum(x => x.Amount)));
    }

    public Task<RecalculationResult> UpdateRates(
        string employeeId,
        IReadOnlyList<HourlyRate> rates,
        CancellationToken ct)
    {
        if (!employees.TryGetValue(employeeId, out var employee))
        {
            throw DomainException.NotFound(ErrorCodes.EmployeeNotFound, "Сотрудник не найден.");
        }

        var affected = entries.Values.Where(x => x.EmployeeId == employeeId).ToArray();

        // План строится целиком до первой записи — пересчёт не остановится на середине.
        var plan = new List<(TimeEntry Entry, decimal Rate)>();
        long skipped = 0;

        foreach (var entry in affected)
        {
            if (closedPeriods.Contains((entry.Date.Year, entry.Date.Month)))
            {
                skipped++;
                continue;
            }

            plan.Add((entry, RateResolver.Resolve(rates, entry.Date)));
        }

        employees[employeeId] = new Employee
        {
            Id = employee.Id,
            FullName = employee.FullName,
            Department = employee.Department,
            Rates = rates.OrderBy(x => x.ValidFrom).ToList(),
        };

        foreach (var (entry, rate) in plan)
        {
            entry.AppliedRate = rate;
            entry.Amount = Money.Calculate(entry.Hours, rate);
            entry.Version++;
            entry.UpdatedAtUtc = DateTime.UtcNow;
        }

        return Task.FromResult(new RecalculationResult(plan.Count, skipped));
    }

    public Task Seed(CancellationToken ct)
    {
        entries.Clear();
        return Task.CompletedTask;
    }

    private static TimeEntry Clone(TimeEntry entry) => new()
    {
        Id = entry.Id,
        EmployeeId = entry.EmployeeId,
        ProjectId = entry.ProjectId,
        Date = entry.Date,
        Hours = entry.Hours,
        Comment = entry.Comment,
        AppliedRate = entry.AppliedRate,
        Amount = entry.Amount,
        Version = entry.Version,
        CreatedAtUtc = entry.CreatedAtUtc,
        UpdatedAtUtc = entry.UpdatedAtUtc,
    };
}
