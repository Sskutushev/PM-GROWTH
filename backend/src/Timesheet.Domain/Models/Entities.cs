namespace Timesheet.Domain.Models;

/// <summary>Ставка действует с <paramref name="ValidFrom"/> до начала следующей.</summary>
public sealed record HourlyRate(DateOnly ValidFrom, decimal Value);

/// <summary>
/// История ставок лежит внутри сотрудника: она мала, читается всегда целиком
/// и меняется только вместе с ним. Отдельная коллекция дала бы join без выигрыша.
/// </summary>
public sealed class Employee
{
    public required string Id { get; init; }

    public required string FullName { get; init; }

    public required string Department { get; init; }

    /// <summary>Порядок не гарантируется — упорядочивает <c>RateResolver</c>.</summary>
    public List<HourlyRate> Rates { get; init; } = [];
}

public sealed class Project
{
    public required string Id { get; init; }

    /// <summary>Шифр вида «П-001». Уникальность держит индекс в Mongo.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }

    public decimal Budget { get; init; }

    public DateOnly StartDate { get; init; }

    /// <summary><c>null</c> — проект бессрочный.</summary>
    public DateOnly? EndDate { get; init; }
}

public sealed class TimeEntry
{
    public required string Id { get; init; }

    public required string EmployeeId { get; set; }

    public required string ProjectId { get; set; }

    /// <summary>Бизнес-дата без времени: сутки табеля не зависят от таймзоны сервера.</summary>
    public DateOnly Date { get; set; }

    public decimal Hours { get; set; }

    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Ставка на момент расчёта. Денормализована, чтобы месячный отчёт был
    /// <c>$match + $group</c> без join к истории ставок. Синхронизируется при изменении истории.
    /// </summary>
    public decimal AppliedRate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Оптимистическая блокировка: растёт на каждой успешной записи.</summary>
    public long Version { get; set; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record ClosedPeriod(int Year, int Month);
