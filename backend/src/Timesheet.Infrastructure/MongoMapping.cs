using MongoDB.Bson;
using Timesheet.Domain.Models;
namespace Timesheet.Infrastructure;

internal static class MongoMapping
{
    internal static DateTime ToUtc(DateOnly value) => DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    internal static DateOnly ToDate(BsonValue value) => DateOnly.FromDateTime(value.ToUniversalTime());
    internal static decimal Decimal(BsonValue value) => Decimal128.ToDecimal(value.AsDecimal128);
    internal static BsonDecimal128 Decimal(decimal value) => new(value);
    internal static Employee Employee(BsonDocument x) => new() { Id = x["_id"].AsString, FullName = x["fullName"].AsString, Department = x["department"].AsString, Rates = x["rates"].AsBsonArray.Select(r => new HourlyRate(ToDate(r["validFrom"]), Decimal(r["value"]))).ToList() };
    internal static Project Project(BsonDocument x) => new() { Id = x["_id"].AsString, Code = x["code"].AsString, Name = x["name"].AsString, Budget = Decimal(x["budget"]), StartDate = ToDate(x["startDate"]), EndDate = x.TryGetValue("endDate", out var end) && !end.IsBsonNull ? ToDate(end) : null };
    internal static TimeEntry Entry(BsonDocument x) => new() { Id = x["_id"].AsString, EmployeeId = x["employeeId"].AsString, ProjectId = x["projectId"].AsString, Date = ToDate(x["date"]), Hours = Decimal(x["hours"]), AppliedRate = Decimal(x["appliedRate"]), Amount = Decimal(x["amount"]), Comment = x.GetValue("comment", "").AsString, Version = x["version"].ToInt64(), CreatedAtUtc = x["createdAtUtc"].ToUniversalTime(), UpdatedAtUtc = x["updatedAtUtc"].ToUniversalTime() };
    internal static BsonDocument Entry(TimeEntry x) => new() { ["_id"] = x.Id, ["employeeId"] = x.EmployeeId, ["projectId"] = x.ProjectId, ["date"] = ToUtc(x.Date), ["hours"] = Decimal(x.Hours), ["appliedRate"] = Decimal(x.AppliedRate), ["amount"] = Decimal(x.Amount), ["comment"] = x.Comment, ["version"] = x.Version, ["createdAtUtc"] = x.CreatedAtUtc, ["updatedAtUtc"] = x.UpdatedAtUtc };
}
