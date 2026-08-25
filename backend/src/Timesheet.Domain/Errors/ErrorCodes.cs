namespace Timesheet.Domain.Errors;

/// <summary>
/// Коды ошибок API. Клиент ветвится по коду, а не по тексту: текст переписывают и переводят,
/// код — часть контракта.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string RateNotFound = "RATE_NOT_FOUND";
    public const string RateHistoryInvalid = "RATE_HISTORY_INVALID";
    public const string DailyHoursExceeded = "DAILY_HOURS_EXCEEDED";
    public const string PeriodClosed = "PERIOD_CLOSED";
    public const string DateOutsideProjectPeriod = "DATE_OUTSIDE_PROJECT_PERIOD";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string TimeEntryNotFound = "TIME_ENTRY_NOT_FOUND";
    public const string EmployeeNotFound = "EMPLOYEE_NOT_FOUND";
    public const string ProjectNotFound = "PROJECT_NOT_FOUND";
    public const string BrokenReference = "BROKEN_REFERENCE";

    /// <summary>Единственный код, который сопровождается HTTP 500.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
