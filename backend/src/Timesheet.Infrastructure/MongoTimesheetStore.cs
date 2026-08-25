using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;

namespace Timesheet.Infrastructure;

/// <summary>
/// The storage port on top of the official driver. No ORM: aggregations are written out in
/// full, so it is visible which query reaches the database and which index it will use.
/// </summary>
public sealed class MongoTimesheetStore : ITimesheetStore
{
    private readonly IMongoDatabase database;

    public MongoTimesheetStore(IMongoClient client, IOptions<MongoOptions> options) =>
        database = client.GetDatabase(options.Value.Database);

    private IMongoCollection<BsonDocument> Entries => database.GetCollection<BsonDocument>(MongoCollections.TimeEntries);

    private IMongoCollection<BsonDocument> Employees_ => database.GetCollection<BsonDocument>(MongoCollections.Employees);

    private IMongoCollection<BsonDocument> Projects_ => database.GetCollection<BsonDocument>(MongoCollections.Projects);

    private IMongoCollection<BsonDocument> ClosedPeriods =>
        database.GetCollection<BsonDocument>(MongoCollections.ClosedPeriods);

    // ---------- Catalogues ----------

    public async Task<Employee?> GetEmployee(string id, CancellationToken ct)
    {
        var document = await Employees_
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .FirstOrDefaultAsync(ct);

        return document is null ? null : MongoMapping.Employee(document);
    }

    public async Task<Project?> GetProject(string id, CancellationToken ct)
    {
        var document = await Projects_
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .FirstOrDefaultAsync(ct);

        return document is null ? null : MongoMapping.Project(document);
    }

    public async Task<IReadOnlyList<LookupItem>> Employees(CancellationToken ct)
    {
        var documents = await Employees_
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("fullName"))
            .ToListAsync(ct);

        return documents
            .Select(x => new LookupItem(x["_id"].AsString, string.Empty, x["fullName"].AsString))
            .ToArray();
    }

    public async Task<IReadOnlyList<LookupItem>> Projects(CancellationToken ct)
    {
        var documents = await Projects_
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("code"))
            .ToListAsync(ct);

        return documents
            .Select(x => new LookupItem(x["_id"].AsString, x["code"].AsString, x["name"].AsString))
            .ToArray();
    }

    // ---------- Periods ----------

    public Task<bool> IsPeriodClosed(DateOnly date, CancellationToken ct) =>
        ClosedPeriods
            .Find(new BsonDocument { ["year"] = date.Year, ["month"] = date.Month })
            .AnyAsync(ct);

    public async Task SetPeriod(int year, int month, bool closed, CancellationToken ct)
    {
        var filter = new BsonDocument { ["year"] = year, ["month"] = month };

        if (closed)
        {
            await ClosedPeriods.ReplaceOneAsync(
                filter,
                new BsonDocument { ["year"] = year, ["month"] = month },
                new ReplaceOptions { IsUpsert = true },
                ct);
        }
        else
        {
            await ClosedPeriods.DeleteOneAsync(filter, ct);
        }
    }

    // ---------- Time entries ----------

    public async Task<TimeEntry?> GetEntry(string id, CancellationToken ct)
    {
        var document = await Entries
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .FirstOrDefaultAsync(ct);

        return document is null ? null : MongoMapping.Entry(document);
    }

    public async Task<decimal> GetDailyHours(string employeeId, DateOnly date, string? excludingId, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("employeeId", employeeId)
            & Builders<BsonDocument>.Filter.Eq("date", MongoMapping.ToUtc(date));

        if (excludingId is not null)
        {
            filter &= Builders<BsonDocument>.Filter.Ne("_id", excludingId);
        }

        var totals = await Entries
            .Aggregate()
            .Match(filter)
            .Group(new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["total"] = new BsonDocument("$sum", "$hours"),
            })
            .ToListAsync(ct);

        return totals.Count == 0 ? 0m : MongoMapping.Decimal(totals[0]["total"]);
    }

    public async Task<TimeEntry> Insert(TimeEntry entry, CancellationToken ct)
    {
        await Entries.InsertOneAsync(MongoMapping.Entry(entry), cancellationToken: ct);
        return entry;
    }

    public async Task<TimeEntry?> Replace(TimeEntry entry, long expectedVersion, CancellationToken ct)
    {
        // The version sits in the filter, so the check and the write are atomic in Mongo.
        var filter = Builders<BsonDocument>.Filter.Eq("_id", entry.Id)
            & Builders<BsonDocument>.Filter.Eq("version", expectedVersion);

        var result = await Entries.ReplaceOneAsync(filter, MongoMapping.Entry(entry), cancellationToken: ct);

        return result.MatchedCount == 1 ? entry : null;
    }

    public async Task<bool> Delete(string id, long? expectedVersion, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id);

        if (expectedVersion is not null)
        {
            filter &= Builders<BsonDocument>.Filter.Eq("version", expectedVersion);
        }

        var result = await Entries.DeleteOneAsync(filter, ct);
        return result.DeletedCount == 1;
    }

    public async Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken ct)
    {
        var filter = BuildListFilter(query);

        var totalCount = await Entries.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await Entries
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("date").Descending("_id"))
            .Skip((query.Page - 1) * query.PageSize)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        var totals = await Entries
            .Aggregate()
            .Match(filter)
            .Group(new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["hours"] = new BsonDocument("$sum", "$hours"),
                ["amount"] = new BsonDocument("$sum", "$amount"),
            })
            .ToListAsync(ct);

        var entries = documents.Select(MongoMapping.Entry).ToArray();

        var employees = (await Employees_.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct))
            .ToDictionary(x => x["_id"].AsString, x => x["fullName"].AsString);

        var projects = (await Projects_.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct))
            .ToDictionary(x => x["_id"].AsString, x => x["code"].AsString);

        var daily = new Dictionary<(string, DateOnly), decimal>();
        foreach (var entry in entries)
        {
            var key = (entry.EmployeeId, entry.Date);
            if (!daily.ContainsKey(key))
            {
                daily[key] = await GetDailyHours(entry.EmployeeId, entry.Date, null, ct);
            }
        }

        var items = entries
            .Select(entry =>
            {
                var dailyHours = daily[(entry.EmployeeId, entry.Date)];

                return new TimeEntryView(
                    entry.Id,
                    entry.EmployeeId,
                    employees.GetValueOrDefault(entry.EmployeeId, "Удалённый сотрудник"),
                    entry.ProjectId,
                    projects.GetValueOrDefault(entry.ProjectId, "Удалённый проект"),
                    entry.Date,
                    entry.Hours,
                    entry.AppliedRate,
                    entry.Amount,
                    entry.Comment,
                    WorkHoursPolicy.IsOvertime(dailyHours),
                    dailyHours,
                    entry.Version);
            })
            .ToArray();

        var totalHours = totals.Count == 0 ? 0m : MongoMapping.Decimal(totals[0]["hours"]);
        var totalAmount = totals.Count == 0 ? 0m : MongoMapping.Decimal(totals[0]["amount"]);

        return new PagedResult<TimeEntryView>(items, query.Page, query.PageSize, totalCount, totalHours, totalAmount);
    }

    // ---------- Report ----------

    public async Task<ProjectReport> Report(int year, int month, CancellationToken ct)
    {
        var range = MonthRange.Create(year, month);

        // The database groups: one row per project reaches the application, not the entries.
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("date", new BsonDocument
            {
                ["$gte"] = MongoMapping.ToUtc(range.Start),
                ["$lt"] = MongoMapping.ToUtc(range.EndExclusive),
            })),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$projectId",
                ["hours"] = new BsonDocument("$sum", "$hours"),
                ["amount"] = new BsonDocument("$sum", "$amount"),
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1)),
        };

        var aggregates = await Entries.Aggregate<BsonDocument>(pipeline).ToListAsync(ct);

        var projects = (await Projects_.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct))
            .ToDictionary(x => x["_id"].AsString, MongoMapping.Project);

        var rows = aggregates
            .Where(x => projects.ContainsKey(x["_id"].AsString))
            .Select(x =>
            {
                var project = projects[x["_id"].AsString];
                var hours = MongoMapping.Decimal(x["hours"]);
                var amount = MongoMapping.Decimal(x["amount"]);
                var budget = BudgetPolicy.Evaluate(amount, project.Budget);

                return new ProjectReportRow(
                    project.Id,
                    project.Code,
                    project.Name,
                    hours,
                    amount,
                    project.Budget,
                    budget.DisplayPercent,
                    budget.IsAtRisk,
                    budget.IsOverspent);
            })
            .OrderBy(x => x.ProjectCode, StringComparer.Ordinal)
            .ToArray();

        return new ProjectReport(rows, rows.Sum(x => x.Hours), rows.Sum(x => x.Amount));
    }

    // ---------- Rates ----------

    public async Task<RecalculationResult> UpdateRates(
        string employeeId,
        IReadOnlyList<HourlyRate> rates,
        CancellationToken ct)
    {
        var updateResult = await Employees_.UpdateOneAsync(
            new BsonDocument("_id", employeeId),
            new BsonDocument("$set", new BsonDocument("rates", MongoMapping.Rates(rates))),
            cancellationToken: ct);

        if (updateResult.MatchedCount == 0)
        {
            throw DomainException.NotFound(ErrorCodes.EmployeeNotFound, "Сотрудник не найден.");
        }

        long recalculated = 0;
        long skipped = 0;

        using var cursor = await Entries.FindAsync(new BsonDocument("employeeId", employeeId), cancellationToken: ct);

        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var document in cursor.Current)
            {
                var entry = MongoMapping.Entry(document);

                if (await IsPeriodClosed(entry.Date, ct))
                {
                    skipped++;
                    continue;
                }

                entry.AppliedRate = RateResolver.Resolve(rates, entry.Date);
                entry.Amount = Money.Calculate(entry.Hours, entry.AppliedRate);
                entry.Version++;
                entry.UpdatedAtUtc = DateTime.UtcNow;

                await Entries.ReplaceOneAsync(
                    new BsonDocument("_id", entry.Id),
                    MongoMapping.Entry(entry),
                    cancellationToken: ct);

                recalculated++;
            }
        }

        return new RecalculationResult(recalculated, skipped);
    }

    // ---------- Maintenance ----------

    public async Task Seed(CancellationToken ct)
    {
        // Clear documents instead of dropping collections: DropCollection takes the collection's
        // indexes with it, and seeding data must not change the storage schema.
        await Entries.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
        await Employees_.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
        await Projects_.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);
        await ClosedPeriods.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);

        // The first run may reach this point before the collections exist; index creation is idempotent.
        await EnsureIndexes(ct);

        var employees = new[]
        {
            new Employee
            {
                Id = "ivanov",
                FullName = "Иванов И. И.",
                Department = "Проектный",
                Rates =
                [
                    new HourlyRate(new DateOnly(2026, 1, 1), 500m),
                    new HourlyRate(new DateOnly(2026, 3, 1), 600m),
                ],
            },
            new Employee
            {
                Id = "petrova",
                FullName = "Петрова А. С.",
                Department = "Проектный",
                Rates = [new HourlyRate(new DateOnly(2026, 2, 1), 700m)],
            },
        };

        var projects = new[]
        {
            new Project
            {
                Id = "p001",
                Code = "П-001",
                Name = "Реконструкция цеха",
                Budget = 20_000m,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 3, 31),
            },
            new Project
            {
                Id = "p002",
                Code = "П-002",
                Name = "Инженерные сети",
                Budget = 5_000m,
                StartDate = new DateOnly(2026, 3, 1),
                EndDate = null,
            },
        };

        await Employees_.InsertManyAsync(employees.Select(MongoMapping.Employee), cancellationToken: ct);
        await Projects_.InsertManyAsync(projects.Select(MongoMapping.Project), cancellationToken: ct);

        var rows = new (string EmployeeId, string ProjectId, DateOnly Date, decimal Hours)[]
        {
            ("ivanov", "p001", new DateOnly(2026, 2, 20), 8m),
            ("ivanov", "p001", new DateOnly(2026, 3, 5), 8m),
            ("petrova", "p001", new DateOnly(2026, 3, 5), 4m),
            ("petrova", "p002", new DateOnly(2026, 3, 6), 10m),
        };

        var now = DateTime.UtcNow;
        var byId = employees.ToDictionary(x => x.Id);

        var documents = rows.Select(row =>
        {
            var rate = RateResolver.Resolve(byId[row.EmployeeId].Rates, row.Date);

            return MongoMapping.Entry(new TimeEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                EmployeeId = row.EmployeeId,
                ProjectId = row.ProjectId,
                Date = row.Date,
                Hours = row.Hours,
                AppliedRate = rate,
                Amount = Money.Calculate(row.Hours, rate),
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        });

        await Entries.InsertManyAsync(documents, cancellationToken: ct);
    }

    public Task EnsureIndexes(CancellationToken ct) => MongoIndexCatalog.EnsureAsync(database, ct);

    public Task<IReadOnlyList<IndexReport>> DescribeIndexes(CancellationToken ct) =>
        MongoIndexCatalog.DescribeAsync(database, ct);

    private static FilterDefinition<BsonDocument> BuildListFilter(TimeEntryQuery query)
    {
        var range = MonthRange.Create(query.Year, query.Month);

        var filter = Builders<BsonDocument>.Filter.Gte("date", MongoMapping.ToUtc(range.Start))
            & Builders<BsonDocument>.Filter.Lt("date", MongoMapping.ToUtc(range.EndExclusive));

        if (!string.IsNullOrWhiteSpace(query.EmployeeId))
        {
            filter &= Builders<BsonDocument>.Filter.Eq("employeeId", query.EmployeeId);
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectId))
        {
            filter &= Builders<BsonDocument>.Filter.Eq("projectId", query.ProjectId);
        }

        return filter;
    }
}
