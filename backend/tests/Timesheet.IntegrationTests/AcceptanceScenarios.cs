using System.Net;
using System.Net.Http.Json;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Models;

namespace Timesheet.IntegrationTests;

// The acceptance checks from the task, driven over HTTP against a real MongoDB.
// The numbers are taken from the task verbatim and must never be "adjusted" to fit the code.
[Collection(ApiCollection.Name)]
public sealed class AcceptanceScenarios(ApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Reports ----------

    [Fact]
    public async Task March_report_matches_the_acceptance_table()
    {
        var report = await Get<ProjectReport>("/api/reports/projects?year=2026&month=3");

        Assert.Equal(22m, report.TotalHours);
        Assert.Equal(14_600m, report.TotalAmount);

        var workshop = report.Items.Single(x => x.ProjectCode == "П-001");
        Assert.Equal(12m, workshop.Hours);
        Assert.Equal(7_600m, workshop.Amount);
        Assert.Equal(20_000m, workshop.Budget);
        Assert.Equal(38m, workshop.Percent);
        Assert.False(workshop.IsOverspent);
        Assert.False(workshop.IsAtRisk);

        var networks = report.Items.Single(x => x.ProjectCode == "П-002");
        Assert.Equal(10m, networks.Hours);
        Assert.Equal(7_000m, networks.Amount);
        Assert.Equal(140m, networks.Percent);
        Assert.True(networks.IsOverspent);
    }

    [Fact]
    public async Task February_report_matches_the_acceptance_table()
    {
        var report = await Get<ProjectReport>("/api/reports/projects?year=2026&month=2");

        Assert.Equal(8m, report.TotalHours);
        Assert.Equal(4_000m, report.TotalAmount);
        Assert.Equal(20m, report.Items.Single().Percent);
    }

    [Fact]
    public async Task Report_only_lists_projects_with_logged_time()
    {
        var report = await Get<ProjectReport>("/api/reports/projects?year=2026&month=1");

        Assert.Empty(report.Items);
        Assert.Equal(0m, report.TotalAmount);
    }

    // ---------- Timesheet listing ----------

    [Fact]
    public async Task Empty_optional_filters_do_not_hide_month_entries()
    {
        var page = await Get<PagedResult<TimeEntryView>>(
            "/api/time-entries?year=2026&month=3&employeeId=&projectId=&page=1&pageSize=50");

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(22m, page.TotalHours);
        Assert.Equal(14_600m, page.TotalAmount);
    }

    [Fact]
    public async Task List_returns_the_rate_applied_on_the_entry_date()
    {
        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=3&employeeId=ivanov");

        var entry = Assert.Single(page.Items);
        Assert.Equal(600m, entry.AppliedRate);
        Assert.Equal(4_800m, entry.Amount);
        Assert.Equal("Иванов И. И.", entry.EmployeeName);
        Assert.Equal("П-001", entry.ProjectCode);
    }

    [Fact]
    public async Task February_entry_keeps_the_old_rate()
    {
        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=2");

        var entry = Assert.Single(page.Items);
        Assert.Equal(500m, entry.AppliedRate);
        Assert.Equal(4_000m, entry.Amount);
    }

    // ---------- Scenario 1: no rate on that date ----------

    [Fact]
    public async Task Scenario_1_entry_before_the_first_rate_is_rejected()
    {
        var response = await Post("/api/time-entries", Entry("petrova", "p001", "2026-01-15", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RATE_NOT_FOUND", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---------- Scenarios 2 and 3: overtime and the daily cap ----------

    [Fact]
    public async Task Scenario_2_twenty_hours_are_saved_and_flagged_as_overtime()
    {
        var created = await Create(Entry("ivanov", "p001", "2026-03-06", 20m));
        Assert.Equal(12_000m, created.Amount);

        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=3&employeeId=ivanov");
        var entry = page.Items.Single(x => x.Id == created.Id);

        Assert.True(entry.IsOvertime);
        Assert.Equal(20m, entry.DailyHours);
    }

    [Fact]
    public async Task Scenario_3_twenty_six_hours_in_one_day_are_rejected()
    {
        await Create(Entry("ivanov", "p001", "2026-03-06", 20m));

        var response = await Post("/api/time-entries", Entry("ivanov", "p001", "2026-03-06", 6m));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("DAILY_HOURS_EXCEEDED", body, StringComparison.Ordinal);
        Assert.Contains("26", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Daily_limit_spans_projects()
    {
        await Create(Entry("ivanov", "p001", "2026-03-06", 20m));

        var response = await Post("/api/time-entries", Entry("ivanov", "p002", "2026-03-06", 6m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Scenario 4: project boundaries ----------

    [Fact]
    public async Task Scenario_4_entry_before_the_project_starts_is_rejected()
    {
        var response = await Post("/api/time-entries", Entry("ivanov", "p002", "2026-02-20", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "DATE_OUTSIDE_PROJECT_PERIOD",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entry_after_the_project_ends_is_rejected()
    {
        var response = await Post("/api/time-entries", Entry("ivanov", "p001", "2026-04-01", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Open_ended_project_accepts_later_dates()
    {
        var created = await Create(Entry("ivanov", "p002", "2026-06-01", 8m));

        Assert.Equal(600m, created.AppliedRate);
    }

    // ---------- Scenario 5: closed period ----------

    [Fact]
    public async Task Scenario_5_entries_in_a_closed_month_cannot_be_changed()
    {
        var february = await SingleFebruaryEntry();

        var closed = await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 2));
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);

        try
        {
            var update = await fixture.Client.PostAsJsonAsync(
                $"/api/time-entries/{february.Id}",
                Entry("ivanov", "p001", "2026-02-20", 4m, february.Version));

            Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
            Assert.Contains("PERIOD_CLOSED", await update.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var delete = await fixture.Client.DeleteAsync($"/api/time-entries/{february.Id}?version={february.Version}");
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

            var create = await Post("/api/time-entries", Entry("ivanov", "p001", "2026-02-21", 4m));
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        }
        finally
        {
            await fixture.Client.PostAsJsonAsync("/api/periods/open", new PeriodRequest(2026, 2));
        }
    }

    [Fact]
    public async Task Reopening_a_month_restores_editing()
    {
        var february = await SingleFebruaryEntry();

        await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 2));
        await fixture.Client.PostAsJsonAsync("/api/periods/open", new PeriodRequest(2026, 2));

        var update = await fixture.Client.PostAsJsonAsync(
            $"/api/time-entries/{february.Id}",
            Entry("ivanov", "p001", "2026-02-20", 4m, february.Version));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    // ---------- Scenario 6: hour format ----------

    [Theory]
    [InlineData(0)]
    [InlineData(3.7)]
    [InlineData(25)]
    public async Task Scenario_6_invalid_hours_are_rejected(decimal hours)
    {
        var response = await Post("/api/time-entries", Entry("ivanov", "p001", "2026-03-06", hours));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---------- Scenario 7: concurrent editing ----------

    [Fact]
    public async Task Scenario_7_second_tab_gets_a_clear_refusal()
    {
        var created = await Create(Entry("ivanov", "p001", "2026-03-10", 8m));

        var firstTab = await fixture.Client.PostAsJsonAsync(
            $"/api/time-entries/{created.Id}",
            Entry("ivanov", "p001", "2026-03-10", 4m, created.Version));
        Assert.Equal(HttpStatusCode.OK, firstTab.StatusCode);

        var secondTab = await fixture.Client.PostAsJsonAsync(
            $"/api/time-entries/{created.Id}",
            Entry("ivanov", "p001", "2026-03-10", 6m, created.Version));

        Assert.Equal(HttpStatusCode.Conflict, secondTab.StatusCode);
        Assert.Contains(
            "CONCURRENCY_CONFLICT",
            await secondTab.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // The other tab's changes were not overwritten.
        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=3&employeeId=ivanov");
        Assert.Equal(4m, page.Items.Single(x => x.Id == created.Id).Hours);
    }

    [Fact]
    public async Task Delete_with_a_stale_version_is_rejected()
    {
        var created = await Create(Entry("ivanov", "p001", "2026-03-10", 8m));

        await fixture.Client.PostAsJsonAsync(
            $"/api/time-entries/{created.Id}",
            Entry("ivanov", "p001", "2026-03-10", 4m, created.Version));

        var delete = await fixture.Client.DeleteAsync($"/api/time-entries/{created.Id}?version={created.Version}");

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_entry_from_the_month()
    {
        var created = await Create(Entry("ivanov", "p001", "2026-03-10", 8m));

        var delete = await fixture.Client.DeleteAsync($"/api/time-entries/{created.Id}?version={created.Version}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=3");
        Assert.DoesNotContain(page.Items, x => x.Id == created.Id);
    }

    // ---------- Scenario 8: retroactive rate change ----------

    [Fact]
    public async Task Scenario_8_retroactive_rate_change_recalculates_march()
    {
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/employees/ivanov/rates",
            new RateUpdateRequest(
            [
                new RateInput(new DateOnly(2026, 1, 1), 500m),
                new RateInput(new DateOnly(2026, 3, 1), 650m),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=3&employeeId=ivanov");
        var entry = Assert.Single(page.Items);

        Assert.Equal(650m, entry.AppliedRate);
        Assert.Equal(5_200m, entry.Amount);

        var report = await Get<ProjectReport>("/api/reports/projects?year=2026&month=3");
        Assert.Equal(8_000m, report.Items.Single(x => x.ProjectCode == "П-001").Amount);
    }

    [Fact]
    public async Task Rate_change_leaves_closed_periods_untouched()
    {
        await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 2));

        try
        {
            var response = await fixture.Client.PostAsJsonAsync(
                "/api/employees/ivanov/rates",
                new RateUpdateRequest(
                [
                    new RateInput(new DateOnly(2026, 1, 1), 550m),
                    new RateInput(new DateOnly(2026, 3, 1), 650m),
                ]));

            var result = await response.Content.ReadFromJsonAsync<RecalculationResult>();
            Assert.NotNull(result);
            Assert.Equal(1, result.SkippedInClosedPeriods);

            var february = await Get<ProjectReport>("/api/reports/projects?year=2026&month=2");
            Assert.Equal(4_000m, february.TotalAmount);
        }
        finally
        {
            await fixture.Client.PostAsJsonAsync("/api/periods/open", new PeriodRequest(2026, 2));
        }
    }

    // ---------- Catalogues ----------

    [Fact]
    public async Task Lookups_expose_seeded_catalogues()
    {
        var employees = await Get<IReadOnlyList<LookupItem>>("/api/employees");
        var projects = await Get<IReadOnlyList<LookupItem>>("/api/projects");

        Assert.Equal(2, employees.Count);
        Assert.Contains(employees, x => x.Name == "Иванов И. И.");
        Assert.Equal(["П-001", "П-002"], projects.Select(x => x.Code));
    }

    // ---------- Helpers ----------

    private static SaveTimeEntryRequest Entry(
        string employeeId,
        string projectId,
        string date,
        decimal hours,
        long? version = null) =>
        new(
            employeeId,
            projectId,
            DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            hours,
            string.Empty,
            version);

    private Task<HttpResponseMessage> Post(string url, SaveTimeEntryRequest request) =>
        fixture.Client.PutAsJsonAsync(url, request);

    private async Task<TimeEntry> Create(SaveTimeEntryRequest request)
    {
        var response = await Post("/api/time-entries", request);
        await EnsureSuccess(response, "/api/time-entries");

        return await response.Content.ReadFromJsonAsync<TimeEntry>()
            ?? throw new InvalidOperationException("Пустой ответ на создание записи.");
    }

    private async Task<T> Get<T>(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        await EnsureSuccess(response, url);

        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Пустой ответ {url}.");
    }

    // The response body goes into the failure message: otherwise a break reads as "500" with no reason.
    private static async Task EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"{(int)response.StatusCode} на {url}: {body}");
    }

    private async Task<TimeEntryView> SingleFebruaryEntry()
    {
        var page = await Get<PagedResult<TimeEntryView>>("/api/time-entries?year=2026&month=2");
        return page.Items.Single();
    }
}
