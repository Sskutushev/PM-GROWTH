namespace Timesheet.Domain.Errors;

/// <summary>
/// The only exception type the API turns into a meaningful response. Everything else
/// is treated as a defect and returned as 500, so every expected failure — including
/// bad input — has to be one of these.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(
        string code,
        string message,
        int status = 400,
        IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        Code = code;
        Status = status;
        Details = details ?? new Dictionary<string, object?>();
    }

    public string Code { get; }

    /// <summary>400 for input, 404 for missing, 409 for a state conflict.</summary>
    public int Status { get; }

    /// <summary>Structured detail for the UI: field name, limits, actual values.</summary>
    public IReadOnlyDictionary<string, object?> Details { get; }

    public static DomainException Validation(string message, string? field = null) => new(
        ErrorCodes.ValidationFailed,
        message,
        400,
        field is null ? null : new Dictionary<string, object?> { ["field"] = field });

    public static DomainException NotFound(string code, string message) => new(code, message, 404);

    public static DomainException Conflict(string code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(code, message, 409, details);
}
