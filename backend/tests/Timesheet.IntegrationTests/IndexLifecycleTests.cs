using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;

namespace Timesheet.IntegrationTests;

// The assignment asks for indexes to be created explicitly and justified. They used to be created
// at startup only, while seeding dropped the collections and their indexes with them. These tests
// hold the invariant: after any maintenance the indexes are in place and the queries use them.
[Collection(ApiCollection.Name)]
public sealed class IndexLifecycleTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] EntryIndexes = ["date_project", "employee_date", "project_date"];

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeding_does_not_drop_indexes()
    {
        var before = await fixture.IndexNames("time_entries");

        await fixture.ResetAsync();

        var after = await fixture.IndexNames("time_entries");

        Assert.Equal(before.OrderBy(x => x, StringComparer.Ordinal), after.OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(EntryIndexes, name => Assert.Contains(name, after));
    }

    [Fact]
    public async Task All_declared_indexes_exist_after_seeding()
    {
        var entryIndexes = await fixture.IndexNames("time_entries");
        Assert.All(EntryIndexes, name => Assert.Contains(name, entryIndexes));

        Assert.Contains("unique_code", await fixture.IndexNames("projects"));
        Assert.Contains("unique_period", await fixture.IndexNames("closed_periods"));
    }

    [Fact]
    public async Task Diagnostics_endpoint_reports_the_same_indexes()
    {
        var reports = await fixture.Client.GetFromJsonAsync<IReadOnlyList<IndexReport>>("/api/diagnostics/indexes");

        Assert.NotNull(reports);

        var entries = reports.Single(x => x.Collection == "time_entries");
        Assert.All(EntryIndexes, name => Assert.Contains(name, entries.Indexes));
    }

    [Fact]
    public async Task Unique_project_code_is_enforced_by_the_database()
    {
        var projects = fixture.Database.GetCollection<BsonDocument>("projects");

        var duplicate = new BsonDocument
        {
            ["_id"] = "p001-copy",
            ["code"] = "П-001",
            ["name"] = "Дубль",
            ["budget"] = new BsonDecimal128(1m),
            ["startDate"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["endDate"] = BsonNull.Value,
        };

        var error = await Assert.ThrowsAsync<MongoWriteException>(() => projects.InsertOneAsync(duplicate));

        Assert.Equal(ServerErrorCategory.DuplicateKey, error.WriteError.Category);
    }

    [Fact]
    public async Task Closed_period_cannot_be_stored_twice()
    {
        await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 4));

        try
        {
            var periods = fixture.Database.GetCollection<BsonDocument>("closed_periods");
            var duplicate = new BsonDocument { ["year"] = 2026, ["month"] = 4 };

            await Assert.ThrowsAsync<MongoWriteException>(() => periods.InsertOneAsync(duplicate));
        }
        finally
        {
            await fixture.Client.PostAsJsonAsync("/api/periods/open", new PeriodRequest(2026, 4));
        }
    }

    [Fact]
    public async Task Monthly_report_query_uses_an_index_scan()
    {
        await SeedManyEntries(5_000);

        var explain = await Explain(
        [
            new BsonDocument("$match", new BsonDocument("date", new BsonDocument
            {
                ["$gte"] = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ["$lt"] = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            })),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$projectId",
                ["hours"] = new BsonDocument("$sum", "$hours"),
                ["amount"] = new BsonDocument("$sum", "$amount"),
            }),
        ]);

        Assert.Contains("IXSCAN", explain, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", explain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daily_limit_query_uses_the_employee_index()
    {
        await SeedManyEntries(5_000);

        var explain = await Explain(
        [
            new BsonDocument("$match", new BsonDocument
            {
                ["employeeId"] = "ivanov",
                ["date"] = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            }),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["total"] = new BsonDocument("$sum", "$hours"),
            }),
        ]);

        Assert.Contains("IXSCAN", explain, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", explain, StringComparison.Ordinal);
    }

    private async Task<string> Explain(BsonDocument[] pipeline)
    {
        var command = new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["aggregate"] = "time_entries",
                ["pipeline"] = new BsonArray(pipeline),
                ["cursor"] = new BsonDocument(),
            },
            ["verbosity"] = "queryPlanner",
        };

        var result = await fixture.Database.RunCommandAsync<BsonDocument>(command);
        return result.ToJson();
    }

    /// <summary>
    /// A query plan over four documents proves nothing: the planner may pick anything.
    /// Fill the collection so that the index choice becomes meaningful.
    /// </summary>
    private async Task SeedManyEntries(int count)
    {
        var collection = fixture.Database.GetCollection<BsonDocument>("time_entries");
        var documents = new List<BsonDocument>(count);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < count; i++)
        {
            documents.Add(new BsonDocument
            {
                ["_id"] = $"bulk-{i}",
                ["employeeId"] = i % 2 == 0 ? "ivanov" : "petrova",
                ["projectId"] = i % 3 == 0 ? "p001" : "p002",
                ["date"] = start.AddDays(i % 120),
                ["hours"] = new BsonDecimal128(0.5m),
                ["appliedRate"] = new BsonDecimal128(500m),
                ["amount"] = new BsonDecimal128(250m),
                ["comment"] = string.Empty,
                ["version"] = 1L,
                ["createdAtUtc"] = start,
                ["updatedAtUtc"] = start,
            });
        }

        await collection.InsertManyAsync(documents);
    }
}
