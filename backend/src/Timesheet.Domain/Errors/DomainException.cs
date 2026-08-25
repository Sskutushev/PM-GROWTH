namespace Timesheet.Domain.Errors;

public sealed class DomainException : Exception
{
    public DomainException(string code, string message, int status = 400, IReadOnlyDictionary<string, object?>? details = null) : base(message)
    {
        Code = code;
        Status = status;
        Details = details ?? new Dictionary<string, object?>();
    }
    public string Code { get; }
    public int Status { get; }
    public IReadOnlyDictionary<string, object?> Details { get; }
}
