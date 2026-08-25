using Timesheet.Application;
using Timesheet.Application.Contracts;

namespace Timesheet.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder api)
    {
        api.MapGet("/reports/projects", GetProjectReport)
            .WithTags("Отчёты")
            .WithSummary("Стоимость трудозатрат по проектам за месяц")
            .Produces<ProjectReport>();

        return api;
    }

    private static Task<ProjectReport> GetProjectReport(
        int year,
        int month,
        TimesheetService service,
        CancellationToken ct) =>
        service.Report(year, month, ct);
}
