using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Models;

namespace Timesheet.IntegrationTests;

// The daily cap spans documents: it is a rule about the sum of every entry of one employee on
// one date. Reading that sum and then inserting is a read-check-write, so two requests could
// both see 8 hours, both accept 10 more and leave the day at 28. These tests fire the requests
// in parallel and assert the invariant holds, not that the code happens to be ordered nicely.
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Two_parallel_creates_cannot_push_a_day_over_the_limit()
    {
        // The reference data already holds 8 hours for Ivanov on this date.
        var date = new DateOnly(2026, 3, 5);

        var responses = await Task.WhenAll(
            Create("ivanov", date, 10m),
            Create("ivanov", date, 10m));

        Assert.Equal(1, responses.Count(x => x.IsSuccessStatusCode));

        var rejected = responses.Single(x => !x.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        Assert.Equal(18m, await StoredHours("ivanov", date));
    }

    [Fact]
    public async Task A_crowd_of_parallel_creates_stops_exactly_at_the_limit()
    {
        // An empty day inside the project period: eight requests of four hours compete for the
        // 24 the day holds, so six of them can win and no combination of winners exceeds it.
        var date = new DateOnly(2026, 3, 20);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Create("petrova", date, 4m)));

        var accepted = responses.Count(x => x.IsSuccessStatusCode);

        Assert.Equal(6, accepted);
        Assert.All(
            responses.Where(x => !x.IsSuccessStatusCode),
            x => Assert.Equal(HttpStatusCode.Conflict, x.StatusCode));

        Assert.Equal(24m, await StoredHours("petrova", date));
    }

    [Fact]
    public async Task A_rejected_create_reserves_nothing()
    {
        var date = new DateOnly(2026, 3, 5);

        var rejected = await Create("ivanov", date, 20m);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // The day still holds only what the seed put there, and the guard agrees with it.
        Assert.Equal(8m, await StoredHours("ivanov", date));
        Assert.Equal(8m, await GuardHours("ivanov", date));
    }

    [Fact]
    public async Task The_guard_still_matches_the_entries_after_a_mix_of_writes()
    {
        var date = new DateOnly(2026, 3, 12);

        var first = await Created("ivanov", date, 6m);
        var second = await Created("ivanov", date, 4m);

        // Edit one entry, move the other to a different day, then delete a third.
        await Edit(first, date, 8m);
        await Edit(second, new DateOnly(2026, 3, 13), 4m);

        var third = await Created("ivanov", date, 2m);
        var deleted = await fixture.Client.DeleteAsync(
            $"/api/time-entries/{third.Id}?version={third.Version}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await AssertGuardMatchesEntries();
    }

    [Fact]
    public async Task Parallel_edits_of_the_same_entry_leave_one_winner()
    {
        var date = new DateOnly(2026, 3, 18);
        var entry = await Created("ivanov", date, 4m);

        var responses = await Task.WhenAll(
            Edit(entry, date, 6m),
            Edit(entry, date, 8m));

        Assert.Equal(1, responses.Count(x => x.IsSuccessStatusCode));
        Assert.Equal(HttpStatusCode.Conflict, responses.Single(x => !x.IsSuccessStatusCode).StatusCode);

        await AssertGuardMatchesEntries();
    }

    // ---------- Helpers ----------

    private Task<HttpResponseMessage> Create(string employeeId, DateOnly date, decimal hours) =>
        fixture.Client.PutAsJsonAsync(
            "/api/time-entries",
            new SaveTimeEntryRequest(employeeId, "p001", date, hours, "нагрузочная проверка"));

    private async Task<TimeEntry> Created(string employeeId, DateOnly date, decimal hours)
    {
        var response = await Create(employeeId, date, hours);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TimeEntry>()
            ?? throw new InvalidOperationException("Пустой ответ на создание записи.");
    }

    private Task<HttpResponseMessage> Edit(TimeEntry entry, DateOnly date, decimal hours) =>
        fixture.Client.PostAsJsonAsync(
            $"/api/time-entries/{entry.Id}",
            new SaveTimeEntryRequest(entry.EmployeeId, entry.ProjectId, date, hours, entry.Comment, entry.Version));

    /// <summary>Hours the entries themselves add up to — the source of truth.</summary>
    private async Task<decimal> StoredHours(string employeeId, DateOnly date)
    {
        var totals = await Entries()
            .Aggregate()
            .Match(new BsonDocument
            {
                ["employeeId"] = employeeId,
                ["date"] = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            })
            .Group(new BsonDocument
            {
                ["_id"] = BsonNull.Value,
                ["total"] = new BsonDocument("$sum", "$hours"),
            })
            .ToListAsync();

        return totals.Count == 0 ? 0m : Decimal128.ToDecimal(totals[0]["total"].AsDecimal128);
    }

    /// <summary>Hours the guard document believes the day holds.</summary>
    private async Task<decimal> GuardHours(string employeeId, DateOnly date)
    {
        var document = await fixture.Database
            .GetCollection<BsonDocument>("daily_hours")
            .Find(new BsonDocument("_id", $"{employeeId}|{date:yyyy-MM-dd}"))
            .FirstOrDefaultAsync();

        return document is null ? 0m : Decimal128.ToDecimal(document["totalHours"].AsDecimal128);
    }

    // Drift between the guard and the entries would either block legal writes or let the cap
    // slip, and neither would be visible from the API. So it is asserted directly.
    private async Task AssertGuardMatchesEntries()
    {
        var entries = await Entries()
            .Aggregate()
            .Group(new BsonDocument
            {
                ["_id"] = new BsonDocument { ["employeeId"] = "$employeeId", ["date"] = "$date" },
                ["total"] = new BsonDocument("$sum", "$hours"),
            })
            .ToListAsync();

        foreach (var row in entries)
        {
            var key = row["_id"].AsBsonDocument;
            var employeeId = key["employeeId"].AsString;
            var date = DateOnly.FromDateTime(key["date"].ToUniversalTime());

            Assert.Equal(
                Decimal128.ToDecimal(row["total"].AsDecimal128),
                await GuardHours(employeeId, date));
        }
    }

    private IMongoCollection<BsonDocument> Entries() =>
        fixture.Database.GetCollection<BsonDocument>("time_entries");
}
