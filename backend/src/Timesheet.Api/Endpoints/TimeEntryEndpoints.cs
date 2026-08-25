using Timesheet.Application;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Models;

namespace Timesheet.Api.Endpoints;

/// <summary>
/// Маршруты табеля. Эндпоинт делает ровно три вещи: принимает параметры,
/// вызывает прикладной сервис и выбирает HTTP-статус успеха.
/// Ни валидации, ни бизнес-правил, ни работы с БД здесь нет.
/// </summary>
public static class TimeEntryEndpoints
{
    public static IEndpointRouteBuilder MapTimeEntries(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/time-entries").WithTags("Табель");

        group.MapGet("/", List)
            .WithSummary("Постраничный список записей за месяц")
            .Produces<PagedResult<TimeEntryView>>();

        group.MapPut("/", Create)
            .WithSummary("Создать запись")
            .Produces<TimeEntry>(StatusCodes.Status201Created);

        group.MapPost("/{id}", Update)
            .WithSummary("Изменить запись (требуется version)")
            .Produces<TimeEntry>();

        group.MapDelete("/{id}", Delete)
            .WithSummary("Удалить запись")
            .Produces(StatusCodes.Status204NoContent);

        return api;
    }

    private static Task<PagedResult<TimeEntryView>> List(
        int year,
        int month,
        string? employeeId,
        string? projectId,
        int? page,
        int? pageSize,
        TimesheetService service,
        CancellationToken ct)
    {
        var query = new TimeEntryQuery(
            year,
            month,
            employeeId,
            projectId,
            page ?? 1,
            pageSize ?? Paging.DefaultPageSize);

        return service.List(query, ct);
    }

    private static async Task<IResult> Create(
        SaveTimeEntryRequest request,
        TimesheetService service,
        CancellationToken ct)
    {
        var created = await service.Create(request, ct);
        return Results.Created($"/api/time-entries/{created.Id}", created);
    }

    private static Task<TimeEntry> Update(
        string id,
        SaveTimeEntryRequest request,
        TimesheetService service,
        CancellationToken ct) =>
        service.Update(id, request, ct);

    private static async Task<IResult> Delete(
        string id,
        long? version,
        TimesheetService service,
        CancellationToken ct)
    {
        await service.Delete(id, version, ct);
        return Results.NoContent();
    }
}
