namespace Timesheet.Domain.Errors;

/// <summary>
/// Единственный тип исключения, который API переводит в осмысленный ответ.
/// Всё остальное считается дефектом кода и отдаётся как 500, поэтому любая
/// ожидаемая ошибка — включая ошибки входных данных — обязана быть именно им.
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

    /// <summary>400 — данные, 404 — не найдено, 409 — конфликт состояния.</summary>
    public int Status { get; }

    /// <summary>Подробности для UI: имя поля, лимиты, фактические значения.</summary>
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
