namespace Timesheet.Application.Contracts;

// ---------- Запись табеля ----------

/// <summary>
/// Тело запроса на создание и изменение записи.
/// <see cref="Version"/> обязателен при изменении: это версия, которую клиент видел,
/// когда открывал форму. По ней ловится конкурентное редактирование.
/// </summary>
public sealed record SaveTimeEntryRequest(
    string EmployeeId,
    string ProjectId,
    DateOnly Date,
    decimal Hours,
    string Comment,
    long? Version = null);

/// <summary>Строка табеля в том виде, в котором её показывает интерфейс.</summary>
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

/// <summary>Границы пагинации. Верхний предел защищает базу от запроса «отдай мне весь месяц одной страницей».</summary>
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
/// Страница результатов. Итоги (<see cref="TotalHours"/>, <see cref="TotalAmount"/>) считаются
/// отдельной агрегацией по полному фильтру, а не по строкам страницы: иначе при пагинации
/// пользователь видел бы итог одной страницы под видом итога месяца.
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

// ---------- Отчёт ----------

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

// ---------- Справочники ----------

public sealed record LookupItem(string Id, string Code, string Name);

// ---------- Ставки ----------

public sealed record RateUpdateRequest(IReadOnlyList<RateInput> Rates);

public sealed record RateInput(DateOnly ValidFrom, decimal Value);

public sealed record RecalculationResult(long Recalculated, long SkippedInClosedPeriods);

// ---------- Периоды ----------

/// <summary>
/// Тело запроса закрытия и открытия месяца.
/// Живёт в Application, а не в Program.cs: у контракта запроса есть валидатор, и они должны быть рядом.
/// </summary>
public sealed record PeriodRequest(int Year, int Month);
