using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;
namespace Timesheet.Infrastructure;

public sealed class MongoTimesheetStore : ITimesheetStore
{
    private readonly IMongoDatabase db;
    private IMongoCollection<BsonDocument> Entries => db.GetCollection<BsonDocument>("time_entries");
    private IMongoCollection<BsonDocument> EmployeesCollection => db.GetCollection<BsonDocument>("employees");
    private IMongoCollection<BsonDocument> ProjectsCollection => db.GetCollection<BsonDocument>("projects");
    public MongoTimesheetStore(IMongoClient client, IOptions<MongoOptions> options) => db = client.GetDatabase(options.Value.Database);

    public async Task<Employee?> GetEmployee(string id, CancellationToken ct) { var x = await EmployeesCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(ct); return x is null ? null : MongoMapping.Employee(x); }
    public async Task<Project?> GetProject(string id, CancellationToken ct) { var x = await ProjectsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(ct); return x is null ? null : MongoMapping.Project(x); }
    public async Task<TimeEntry?> GetEntry(string id, CancellationToken ct) { var x = await Entries.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(ct); return x is null ? null : MongoMapping.Entry(x); }
    public Task<bool> IsPeriodClosed(DateOnly date, CancellationToken ct) => db.GetCollection<BsonDocument>("closed_periods").Find(new BsonDocument { ["year"] = date.Year, ["month"] = date.Month }).AnyAsync(ct);
    public async Task<decimal> GetDailyHours(string employeeId, DateOnly date, string? excludingId, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("employeeId", employeeId) & Builders<BsonDocument>.Filter.Eq("date", MongoMapping.ToUtc(date));
        if (excludingId is not null) filter &= Builders<BsonDocument>.Filter.Ne("_id", excludingId);
        var rows = await Entries.Aggregate().Match(filter).Group(new BsonDocument { ["_id"] = BsonNull.Value, ["total"] = new BsonDocument("$sum", "$hours") }).ToListAsync(ct);
        return rows.Count == 0 ? 0 : MongoMapping.Decimal(rows[0]["total"]);
    }
    public async Task<TimeEntry> Insert(TimeEntry entry, CancellationToken ct) { await Entries.InsertOneAsync(MongoMapping.Entry(entry), cancellationToken: ct); return entry; }
    public async Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", entry.Id) & Builders<BsonDocument>.Filter.Eq("version", expectedVersion);
        var result = await Entries.ReplaceOneAsync(filter, MongoMapping.Entry(entry), cancellationToken: ct);
        return result.ModifiedCount == 1 ? entry : null;
    }
    public async Task<bool> Delete(string id, long? version, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id);
        if (version is not null) filter &= Builders<BsonDocument>.Filter.Eq("version", version);
        return (await Entries.DeleteOneAsync(filter, ct)).DeletedCount == 1;
    }
    public async Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken ct)
    {
        var range = MonthRange.Create(query.Year, query.Month);
        var filter = Builders<BsonDocument>.Filter.Gte("date", MongoMapping.ToUtc(range.Start)) & Builders<BsonDocument>.Filter.Lt("date", MongoMapping.ToUtc(range.EndExclusive));
        if (!string.IsNullOrWhiteSpace(query.EmployeeId)) filter &= Builders<BsonDocument>.Filter.Eq("employeeId", query.EmployeeId);
        if (!string.IsNullOrWhiteSpace(query.ProjectId)) filter &= Builders<BsonDocument>.Filter.Eq("projectId", query.ProjectId);
        var totalCount = await Entries.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await Entries.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("date").Descending("_id")).Skip((query.Page - 1) * query.PageSize).Limit(query.PageSize).ToListAsync(ct);
        var totals = await Entries.Aggregate().Match(filter).Group(new BsonDocument { ["_id"] = BsonNull.Value, ["hours"] = new BsonDocument("$sum", "$hours"), ["amount"] = new BsonDocument("$sum", "$amount") }).ToListAsync(ct);
        var employees = (await EmployeesCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct)).ToDictionary(x => x["_id"].AsString, x => x["fullName"].AsString);
        var projects = (await ProjectsCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct)).ToDictionary(x => x["_id"].AsString, x => x["code"].AsString);
        var daily = new Dictionary<(string, DateOnly), decimal>();
        foreach (var doc in docs) { var entry = MongoMapping.Entry(doc); var key = (entry.EmployeeId, entry.Date); if (!daily.ContainsKey(key)) daily[key] = await GetDailyHours(entry.EmployeeId, entry.Date, null, ct); }
        var items = docs.Select(MongoMapping.Entry).Select(x => new TimeEntryView(x.Id, x.EmployeeId, employees.GetValueOrDefault(x.EmployeeId, "Удалённый сотрудник"), x.ProjectId, projects.GetValueOrDefault(x.ProjectId, "Удалённый проект"), x.Date, x.Hours, x.AppliedRate, x.Amount, x.Comment, WorkHoursPolicy.IsOvertime(daily[(x.EmployeeId, x.Date)]), daily[(x.EmployeeId, x.Date)], x.Version)).ToArray();
        var totalHours = totals.Count == 0 ? 0 : MongoMapping.Decimal(totals[0]["hours"]); var totalAmount = totals.Count == 0 ? 0 : MongoMapping.Decimal(totals[0]["amount"]);
        return new(items, query.Page, query.PageSize, totalCount, totalHours, totalAmount);
    }
    public async Task<ProjectReport> Report(int year, int month, CancellationToken ct)
    {
        var range = MonthRange.Create(year, month);
        var pipeline = new[] { new BsonDocument("$match", new BsonDocument("date", new BsonDocument { ["$gte"] = MongoMapping.ToUtc(range.Start), ["$lt"] = MongoMapping.ToUtc(range.EndExclusive) })), new BsonDocument("$group", new BsonDocument { ["_id"] = "$projectId", ["hours"] = new BsonDocument("$sum", "$hours"), ["amount"] = new BsonDocument("$sum", "$amount") }), new BsonDocument("$sort", new BsonDocument("_id", 1)) };
        var aggregates = await Entries.Aggregate<BsonDocument>(pipeline).ToListAsync(ct);
        var projects = (await ProjectsCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct)).ToDictionary(x => x["_id"].AsString, MongoMapping.Project);
        var rows = aggregates.Where(x => projects.ContainsKey(x["_id"].AsString)).Select(x => { var project = projects[x["_id"].AsString]; var hours = MongoMapping.Decimal(x["hours"]); var amount = MongoMapping.Decimal(x["amount"]); var budget = BudgetPolicy.Evaluate(amount, project.Budget); return new ProjectReportRow(project.Id, project.Code, project.Name, hours, amount, project.Budget, budget.DisplayPercent, budget.IsAtRisk, budget.IsOverspent); }).OrderBy(x => x.ProjectCode).ToArray();
        return new(rows, rows.Sum(x => x.Hours), rows.Sum(x => x.Amount));
    }
    public async Task<IReadOnlyList<LookupItem>> Employees(CancellationToken ct) => (await EmployeesCollection.Find(FilterDefinition<BsonDocument>.Empty).Sort("{fullName:1}").ToListAsync(ct)).Select(x => new LookupItem(x["_id"].AsString, "", x["fullName"].AsString)).ToArray();
    public async Task<IReadOnlyList<LookupItem>> Projects(CancellationToken ct) => (await ProjectsCollection.Find(FilterDefinition<BsonDocument>.Empty).Sort("{code:1}").ToListAsync(ct)).Select(x => new LookupItem(x["_id"].AsString, x["code"].AsString, x["name"].AsString)).ToArray();
    public async Task SetPeriod(int year, int month, bool closed, CancellationToken ct) { var collection = db.GetCollection<BsonDocument>("closed_periods"); var filter = new BsonDocument { ["year"] = year, ["month"] = month }; if (closed) await collection.ReplaceOneAsync(filter, new BsonDocument { ["year"] = year, ["month"] = month }, new ReplaceOptions { IsUpsert = true }, ct); else await collection.DeleteOneAsync(filter, ct); }
    public async Task Seed(CancellationToken ct)
    {
        await db.DropCollectionAsync("time_entries", ct); await db.DropCollectionAsync("employees", ct); await db.DropCollectionAsync("projects", ct); await db.DropCollectionAsync("closed_periods", ct);
        var employees = new[] { EmployeeDoc("ivanov", "Иванов И. И.", new HourlyRate(new(2026, 1, 1), 500m), new HourlyRate(new(2026, 3, 1), 600m)), EmployeeDoc("petrova", "Петрова А. С.", new HourlyRate(new(2026, 2, 1), 700m)) };
        await EmployeesCollection.InsertManyAsync(employees, cancellationToken: ct);
        await ProjectsCollection.InsertManyAsync([ProjectDoc("p001", "П-001", "Реконструкция цеха", 20000m, new(2026, 1, 1), new(2026, 3, 31)), ProjectDoc("p002", "П-002", "Инженерные сети", 5000m, new(2026, 3, 1), null)], cancellationToken: ct);
        var data = new[] { ("ivanov", "p001", new DateOnly(2026, 2, 20), 8m, 500m), ("ivanov", "p001", new DateOnly(2026, 3, 5), 8m, 600m), ("petrova", "p001", new DateOnly(2026, 3, 5), 4m, 700m), ("petrova", "p002", new DateOnly(2026, 3, 6), 10m, 700m) };
        foreach (var x in data) await Insert(new TimeEntry { Id = Guid.NewGuid().ToString("N"), EmployeeId = x.Item1, ProjectId = x.Item2, Date = x.Item3, Hours = x.Item4, AppliedRate = x.Item5, Amount = Money.Calculate(x.Item4, x.Item5), Version = 1, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow }, ct);
    }
    public async Task<RecalculationResult> UpdateRates(string employeeId, IReadOnlyList<HourlyRate> rates, CancellationToken ct)
    {
        var rateDocs = new BsonArray(rates.OrderBy(x => x.ValidFrom).Select(x => new BsonDocument { ["validFrom"] = MongoMapping.ToUtc(x.ValidFrom), ["value"] = MongoMapping.Decimal(x.Value) }));
        if ((await EmployeesCollection.UpdateOneAsync(new BsonDocument("_id", employeeId), new BsonDocument("$set", new BsonDocument("rates", rateDocs)), cancellationToken: ct)).MatchedCount == 0) throw new DomainException("EMPLOYEE_NOT_FOUND", "Сотрудник не найден.", 404);
        long updated = 0, skipped = 0; using var cursor = await Entries.FindAsync(new BsonDocument("employeeId", employeeId), cancellationToken: ct);
        while (await cursor.MoveNextAsync(ct)) foreach (var doc in cursor.Current) { var entry = MongoMapping.Entry(doc); if (await IsPeriodClosed(entry.Date, ct)) { skipped++; continue; } entry.AppliedRate = RateResolver.Resolve(rates, entry.Date); entry.Amount = Money.Calculate(entry.Hours, entry.AppliedRate); entry.Version++; entry.UpdatedAtUtc = DateTime.UtcNow; await Entries.ReplaceOneAsync(new BsonDocument("_id", entry.Id), MongoMapping.Entry(entry), cancellationToken: ct); updated++; }
        return new(updated, skipped);
    }
    private static BsonDocument EmployeeDoc(string id, string name, params HourlyRate[] rates) => new() { ["_id"] = id, ["fullName"] = name, ["department"] = "Проектный", ["rates"] = new BsonArray(rates.Select(x => new BsonDocument { ["validFrom"] = MongoMapping.ToUtc(x.ValidFrom), ["value"] = MongoMapping.Decimal(x.Value) })) };
    private static BsonDocument ProjectDoc(string id, string code, string name, decimal budget, DateOnly start, DateOnly? end) => new() { ["_id"] = id, ["code"] = code, ["name"] = name, ["budget"] = MongoMapping.Decimal(budget), ["startDate"] = MongoMapping.ToUtc(start), ["endDate"] = end is null ? BsonNull.Value : MongoMapping.ToUtc(end.Value) };
}
