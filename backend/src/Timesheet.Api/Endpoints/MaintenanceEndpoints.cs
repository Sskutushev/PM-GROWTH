using Timesheet.Application;

namespace Timesheet.Api.Endpoints;

/// <summary>Maintenance operations: seeding the reference data and inspecting indexes.</summary>
public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenance(this IEndpointRouteBuilder api)
    {
        api.MapPost("/seed", Seed)
            .WithTags("Обслуживание")
            .WithSummary("Заполнить базу контрольными данными из задания")
            .Produces(StatusCodes.Status204NoContent);

        return api;
    }

    private static async Task<IResult> Seed(TimesheetService service, CancellationToken ct)
    {
        await service.Seed(ct);
        return Results.NoContent();
    }
}
