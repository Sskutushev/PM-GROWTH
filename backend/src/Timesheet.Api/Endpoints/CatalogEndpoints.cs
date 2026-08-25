using Timesheet.Application;
using Timesheet.Application.Contracts;

namespace Timesheet.Api.Endpoints;

/// <summary>Catalogues, periods and rate history.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogs(this IEndpointRouteBuilder api)
    {
        api.MapGet("/employees", (TimesheetService service, CancellationToken ct) => service.Employees(ct))
            .WithTags("Справочники")
            .WithSummary("Сотрудники для выпадающего списка")
            .Produces<IReadOnlyList<LookupItem>>();

        api.MapGet("/projects", (TimesheetService service, CancellationToken ct) => service.Projects(ct))
            .WithTags("Справочники")
            .WithSummary("Проекты для выпадающего списка")
            .Produces<IReadOnlyList<LookupItem>>();

        api.MapPost("/employees/{id}/rates", UpdateRates)
            .WithTags("Справочники")
            .WithSummary("Заменить историю ставок и пересчитать открытые записи")
            .Produces<RecalculationResult>();

        return api;
    }

    public static IEndpointRouteBuilder MapPeriods(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/periods").WithTags("Периоды");

        group.MapPost("/close", (PeriodRequest request, TimesheetService service, CancellationToken ct) =>
                SetPeriod(request, closed: true, service, ct))
            .WithSummary("Закрыть месяц");

        group.MapPost("/open", (PeriodRequest request, TimesheetService service, CancellationToken ct) =>
                SetPeriod(request, closed: false, service, ct))
            .WithSummary("Открыть месяц");

        return api;
    }

    private static Task<RecalculationResult> UpdateRates(
        string id,
        RateUpdateRequest request,
        TimesheetService service,
        CancellationToken ct) =>
        service.UpdateRates(id, request, ct);

    private static async Task<IResult> SetPeriod(
        PeriodRequest request,
        bool closed,
        TimesheetService service,
        CancellationToken ct)
    {
        await service.SetPeriod(request.Year, request.Month, closed, ct);
        return Results.NoContent();
    }
}
