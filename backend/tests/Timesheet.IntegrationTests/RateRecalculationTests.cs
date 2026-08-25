using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;

namespace Timesheet.IntegrationTests;

// Recalculation used to write the rate history first and then walk the entries, so a history that
// did not cover an old entry produced a 400 with the new history already stored and part of the
// entries repriced. These tests pin the all-or-nothing behaviour.
[Collection(ApiCollection.Name)]
public sealed class RateRecalculationTests(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rejected_recalculation_changes_nothing()
    {
        var ratesBefore = await StoredRates("ivanov");
        var februaryBefore = await MonthAmount(2026, 2);
        var marchBefore = await MonthAmount(2026, 3);

        // The history starts in March, so the February entry cannot be priced.
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/employees/ivanov/rates",
            new RateUpdateRequest([new RateInput(new DateOnly(2026, 3, 1), 650m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.Equal("RATE_NOT_FOUND", problem?.Code);

        Assert.Equal(ratesBefore, await StoredRates("ivanov"));
        Assert.Equal(februaryBefore, await MonthAmount(2026, 2));
        Assert.Equal(marchBefore, await MonthAmount(2026, 3));
    }

    [Fact]
    public async Task Accepted_recalculation_reprices_every_open_entry()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/employees/ivanov/rates",
            new RateUpdateRequest([new RateInput(new DateOnly(2026, 1, 1), 700m)]));

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RecalculationResult>();
        Assert.NotNull(result);
        Assert.True(result.Recalculated > 0);
        Assert.Equal(0, result.SkippedInClosedPeriods);

        // February in the reference data is Ivanov alone: 8 hours, now at 700 an hour.
        Assert.Equal(5_600m, await MonthAmount(2026, 2));
    }

    [Fact]
    public async Task Closed_periods_are_left_alone()
    {
        var februaryBefore = await MonthAmount(2026, 2);

        await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 2));

        try
        {
            var response = await fixture.Client.PostAsJsonAsync(
                "/api/employees/ivanov/rates",
                new RateUpdateRequest([new RateInput(new DateOnly(2026, 1, 1), 900m)]));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RecalculationResult>();
            Assert.NotNull(result);
            Assert.True(result.SkippedInClosedPeriods > 0);

            Assert.Equal(februaryBefore, await MonthAmount(2026, 2));
        }
        finally
        {
            await fixture.Client.PostAsJsonAsync("/api/periods/open", new PeriodRequest(2026, 2));
        }
    }

    private async Task<IReadOnlyList<string>> StoredRates(string employeeId)
    {
        var employee = await fixture.Database
            .GetCollection<BsonDocument>("employees")
            .Find(new BsonDocument("_id", employeeId))
            .FirstAsync();

        return employee["rates"].AsBsonArray.Select(x => x.ToString()!).ToArray();
    }

    private async Task<decimal> MonthAmount(int year, int month)
    {
        var report = await fixture.Client.GetFromJsonAsync<ProjectReport>(
            $"/api/reports/projects?year={year}&month={month}");

        return report!.TotalAmount;
    }

    private sealed record ProblemPayload(string Code);
}
