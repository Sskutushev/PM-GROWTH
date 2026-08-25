using MongoDB.Bson;
using Timesheet.Domain.Models;

namespace Timesheet.Infrastructure;

// Маппинг домен ↔ BSON руками, а не атрибутами драйвера: схема хранения — деталь
// инфраструктуры, домен не должен обрастать атрибутами Mongo. Заодно в одном месте видно,
// что деньги лежат как Decimal128, а дата — как UTC-полночь.
internal static class MongoMapping
{
    /// <summary>Бизнес-дата хранится как полночь UTC; домен работает с <see cref="DateOnly"/>.</summary>
    internal static DateTime ToUtc(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    internal static DateOnly ToDate(BsonValue value) => DateOnly.FromDateTime(value.ToUniversalTime());

    /// <summary>Деньги и часы — Decimal128: double даёт двоичную погрешность на копейках.</summary>
    internal static decimal Decimal(BsonValue value) => Decimal128.ToDecimal(value.AsDecimal128);

    internal static BsonDecimal128 Decimal(decimal value) => new(value);

    internal static Employee Employee(BsonDocument document) => new()
    {
        Id = document["_id"].AsString,
        FullName = document["fullName"].AsString,
        Department = document["department"].AsString,
        Rates = document["rates"]
            .AsBsonArray
            .Select(rate => new HourlyRate(ToDate(rate["validFrom"]), Decimal(rate["value"])))
            .ToList(),
    };

    internal static Project Project(BsonDocument document) => new()
    {
        Id = document["_id"].AsString,
        Code = document["code"].AsString,
        Name = document["name"].AsString,
        Budget = Decimal(document["budget"]),
        StartDate = ToDate(document["startDate"]),
        EndDate = document.TryGetValue("endDate", out var end) && !end.IsBsonNull
            ? ToDate(end)
            : null,
    };

    internal static TimeEntry Entry(BsonDocument document) => new()
    {
        Id = document["_id"].AsString,
        EmployeeId = document["employeeId"].AsString,
        ProjectId = document["projectId"].AsString,
        Date = ToDate(document["date"]),
        Hours = Decimal(document["hours"]),
        AppliedRate = Decimal(document["appliedRate"]),
        Amount = Decimal(document["amount"]),
        Comment = document.GetValue("comment", string.Empty).AsString,
        Version = document["version"].ToInt64(),
        CreatedAtUtc = document["createdAtUtc"].ToUniversalTime(),
        UpdatedAtUtc = document["updatedAtUtc"].ToUniversalTime(),
    };

    internal static BsonDocument Entry(TimeEntry entry) => new()
    {
        ["_id"] = entry.Id,
        ["employeeId"] = entry.EmployeeId,
        ["projectId"] = entry.ProjectId,
        ["date"] = ToUtc(entry.Date),
        ["hours"] = Decimal(entry.Hours),
        ["appliedRate"] = Decimal(entry.AppliedRate),
        ["amount"] = Decimal(entry.Amount),
        ["comment"] = entry.Comment,
        ["version"] = entry.Version,
        ["createdAtUtc"] = entry.CreatedAtUtc,
        ["updatedAtUtc"] = entry.UpdatedAtUtc,
    };

    internal static BsonDocument Employee(Employee employee) => new()
    {
        ["_id"] = employee.Id,
        ["fullName"] = employee.FullName,
        ["department"] = employee.Department,
        ["rates"] = Rates(employee.Rates),
    };

    internal static BsonArray Rates(IEnumerable<HourlyRate> rates) => new(rates
        .OrderBy(rate => rate.ValidFrom)
        .Select(rate => new BsonDocument
        {
            ["validFrom"] = ToUtc(rate.ValidFrom),
            ["value"] = Decimal(rate.Value),
        }));

    internal static BsonDocument Project(Project project) => new()
    {
        ["_id"] = project.Id,
        ["code"] = project.Code,
        ["name"] = project.Name,
        ["budget"] = Decimal(project.Budget),
        ["startDate"] = ToUtc(project.StartDate),
        ["endDate"] = project.EndDate is null ? BsonNull.Value : ToUtc(project.EndDate.Value),
    };
}
