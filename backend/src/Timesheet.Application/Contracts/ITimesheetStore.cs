using Timesheet.Domain.Models;

namespace Timesheet.Application.Contracts;

/// <summary>
/// Порт хранилища. Application не знает про Mongo и BSON: сценарии гоняются на in-memory
/// реализации, а отчётную часть можно увести в другое хранилище (см. SCALING.md).
/// </summary>
public interface ITimesheetStore
{
    // ---------- Справочники ----------

    Task<Employee?> GetEmployee(string id, CancellationToken cancellationToken);

    Task<Project?> GetProject(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> Employees(CancellationToken cancellationToken);

    Task<IReadOnlyList<LookupItem>> Projects(CancellationToken cancellationToken);

    // ---------- Периоды ----------

    Task<bool> IsPeriodClosed(DateOnly entryDate, CancellationToken cancellationToken);

    Task SetPeriod(int year, int month, bool closed, CancellationToken cancellationToken);

    // ---------- Записи табеля ----------

    Task<TimeEntry?> GetEntry(string id, CancellationToken cancellationToken);

    /// <summary>Часы сотрудника за дату по всем проектам, без учёта изменяемой записи.</summary>
    Task<decimal> GetDailyHours(
        string employeeId,
        DateOnly entryDate,
        string? excludingId,
        CancellationToken cancellationToken);

    Task<TimeEntry> Insert(TimeEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Замена по паре <c>(_id, expectedVersion)</c>. <c>null</c> — версия не совпала,
    /// то есть конкурентное изменение, а не отсутствие записи.
    /// </summary>
    Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken cancellationToken);

    Task<bool> Delete(string id, long? expectedVersion, CancellationToken cancellationToken);

    Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken cancellationToken);

    // ---------- Отчёт ----------

    /// <summary>Группировка выполняется базой: возвращается строка на проект, а не записи табеля.</summary>
    Task<ProjectReport> Report(int year, int month, CancellationToken cancellationToken);

    // ---------- Ставки ----------

    Task<RecalculationResult> UpdateRates(
        string employeeId,
        IReadOnlyList<HourlyRate> rates,
        CancellationToken cancellationToken);

    // ---------- Обслуживание ----------

    Task Seed(CancellationToken cancellationToken);
}
