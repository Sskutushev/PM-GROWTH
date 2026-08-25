using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Timesheet.Application.Contracts;

namespace Timesheet.IntegrationTests;

// ТЗ требует, чтобы ошибка была 400/409 с машиночитаемым кодом и русским текстом,
// а не 500 и не пустое тело. Эти тесты держат именно контракт ответа.
[Collection(ApiCollection.Name)]
public sealed class ErrorContractTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/api/reports/projects?year=2026&month=13")]
    [InlineData("/api/reports/projects?year=2026&month=0")]
    [InlineData("/api/reports/projects?year=1800&month=3")]
    [InlineData("/api/reports/projects?year=9999&month=3")]
    [InlineData("/api/time-entries?year=2026&month=13")]
    [InlineData("/api/time-entries?year=2026&month=-1")]
    public async Task Impossible_month_is_a_400_not_a_500(string url)
    {
        var response = await fixture.Client.GetAsync(url);
        var problem = await ReadProblem(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("VALIDATION_FAILED", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Theory]
    [InlineData("/api/time-entries?year=2026&month=3&pageSize=101")]
    [InlineData("/api/time-entries?year=2026&month=3&pageSize=0")]
    [InlineData("/api/time-entries?year=2026&month=3&page=0")]
    public async Task Pagination_outside_the_contract_is_rejected(string url)
    {
        var response = await fixture.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await ReadProblem(response)).Code);
    }

    [Fact]
    public async Task Missing_required_query_parameters_are_rejected()
    {
        var response = await fixture.Client.GetAsync("/api/time-entries");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unparseable_body_returns_a_coded_400_not_an_empty_response()
    {
        using var content = new StringContent("{ this is not json ", Encoding.UTF8, "application/json");
        var response = await fixture.Client.PutAsync("/api/time-entries", content);
        var problem = await ReadProblem(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Fact]
    public async Task Wrong_field_type_returns_a_coded_400()
    {
        using var content = new StringContent(
            """{"employeeId":"ivanov","projectId":"p001","date":"2026-03-05","hours":"восемь","comment":""}""",
            Encoding.UTF8,
            "application/json");

        var response = await fixture.Client.PutAsync("/api/time-entries", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await ReadProblem(response)).Code);
    }

    [Fact]
    public async Task Validation_error_reports_the_offending_field()
    {
        var response = await fixture.Client.PutAsJsonAsync(
            "/api/time-entries",
            new SaveTimeEntryRequest("", "p001", new DateOnly(2026, 3, 5), 3.7m, ""));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("employeeId", body, StringComparison.Ordinal);
        Assert.Contains("hours", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_entry_is_a_404_with_a_code()
    {
        var response = await fixture.Client.DeleteAsync("/api/time-entries/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("TIME_ENTRY_NOT_FOUND", (await ReadProblem(response)).Code);
    }

    [Fact]
    public async Task Unknown_employee_is_a_404_with_a_code()
    {
        var response = await fixture.Client.PutAsJsonAsync(
            "/api/time-entries",
            new SaveTimeEntryRequest("ghost", "p001", new DateOnly(2026, 3, 5), 8m, ""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("EMPLOYEE_NOT_FOUND", (await ReadProblem(response)).Code);
    }

    [Fact]
    public async Task Unknown_project_is_a_404_with_a_code()
    {
        var response = await fixture.Client.PutAsJsonAsync(
            "/api/time-entries",
            new SaveTimeEntryRequest("ivanov", "ghost", new DateOnly(2026, 3, 5), 8m, ""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PROJECT_NOT_FOUND", (await ReadProblem(response)).Code);
    }

    [Fact]
    public async Task Every_error_carries_a_trace_id()
    {
        var response = await fixture.Client.GetAsync("/api/reports/projects?year=2026&month=13");

        Assert.False(string.IsNullOrWhiteSpace((await ReadProblem(response)).TraceId));
    }

    [Fact]
    public async Task Error_messages_are_in_russian()
    {
        var response = await fixture.Client.GetAsync("/api/reports/projects?year=2026&month=13");
        var title = (await ReadProblem(response)).Title;

        Assert.NotNull(title);
        Assert.Matches("[а-яА-ЯёЁ]", title);
    }

    [Fact]
    public async Task Period_request_validates_the_month()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/periods/close", new PeriodRequest(2026, 13));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        var response = await fixture.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Problem> ReadProblem(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<Problem>(body, Json)
            ?? throw new InvalidOperationException($"Ответ не является ProblemDetails: {body}");
    }

    private sealed record Problem(string? Code, string? Title, int? Status, string? TraceId);
}
