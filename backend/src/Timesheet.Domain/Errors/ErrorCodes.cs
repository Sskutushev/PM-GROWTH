namespace Timesheet.Domain.Errors;

/// <summary>
/// API error codes. Clients branch on the code, never on the message: messages get
/// rewritten and translated, the code is part of the contract.
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

    /// <summary>The only code ever paired with HTTP 500.</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
