using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.MongoDb;
using Timesheet.Application.Contracts;
namespace Timesheet.IntegrationTests;

public sealed class AcceptanceScenarios : IAsyncLifetime
{
    private readonly MongoDbContainer mongo = new MongoDbBuilder().WithImage("mongo:7.0").Build();
    private WebApplicationFactory<Program>? factory;
    private HttpClient Client => factory!.CreateClient();
    public async Task InitializeAsync() { await mongo.StartAsync(); factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("Mongo:ConnectionString", mongo.GetConnectionString())); var seed = await Client.PostAsync("/api/seed", null); seed.EnsureSuccessStatusCode(); }
    public async Task DisposeAsync() { if (factory is not null) await factory.DisposeAsync(); await mongo.DisposeAsync(); }

    [Fact]
    public async Task March_report_matches_acceptance_table()
    {
        var report = await Client.GetFromJsonAsync<ProjectReport>("/api/reports/projects?year=2026&month=3");
        Assert.NotNull(report); Assert.Equal(22m, report.TotalHours); Assert.Equal(14_600m, report.TotalAmount);
        Assert.Contains(report.Items, x => x.ProjectCode == "П-001" && x.Hours == 12m && x.Amount == 7_600m && x.Percent == 38m);
        Assert.Contains(report.Items, x => x.ProjectCode == "П-002" && x.Amount == 7_000m && x.Percent == 140m && x.IsOverspent);
    }
    [Fact]
    public async Task Scenario_1_Petrova_before_first_rate_returns_RATE_NOT_FOUND()
    {
        var response = await Client.PutAsJsonAsync("/api/time-entries", new SaveTimeEntryRequest("petrova", "p001", new(2026, 1, 15), 1m, ""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("RATE_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Scenario_4_P002_before_start_is_rejected()
    {
        var response = await Client.PutAsJsonAsync("/api/time-entries", new SaveTimeEntryRequest("ivanov", "p002", new(2026, 2, 20), 1m, ""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("DATE_OUTSIDE_PROJECT_PERIOD", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Scenario_6_invalid_hours_returns_problem_details()
    {
        var response = await Client.PutAsJsonAsync("/api/time-entries", new SaveTimeEntryRequest("ivanov", "p001", new(2026, 3, 6), 3.7m, ""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
