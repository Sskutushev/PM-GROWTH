using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Timesheet.Application;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<TimesheetService>();
builder.Services.AddMongoInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo { Title = "Timesheet API", Version = "v1", Description = "Учёт трудозатрат и стоимости работ" }));
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().WithOrigins("http://localhost:5173")));
var app = builder.Build();
app.UseMiddleware<ProblemDetailsMiddleware>(); app.UseCors(); app.UseSwagger(); app.UseSwaggerUI();
app.MapHealthChecks("/health/live");
var api = app.MapGroup("/api");
api.MapGet("/time-entries", (int year, int month, string? employeeId, string? projectId, int? page, int? pageSize, TimesheetService service, CancellationToken ct) => service.List(new(year, month, employeeId, projectId, page ?? 1, pageSize ?? 25), ct));
api.MapPut("/time-entries", async (SaveTimeEntryRequest request, TimesheetService service, CancellationToken ct) => Results.Created("/api/time-entries", await service.Create(request, ct)));
api.MapPost("/time-entries/{id}", (string id, SaveTimeEntryRequest request, TimesheetService service, CancellationToken ct) => service.Update(id, request, ct));
api.MapDelete("/time-entries/{id}", async (string id, long? version, TimesheetService service, CancellationToken ct) => { await service.Delete(id, version, ct); return Results.NoContent(); });
api.MapGet("/reports/projects", (int year, int month, TimesheetService service, CancellationToken ct) => service.Report(year, month, ct));
api.MapGet("/employees", (TimesheetService service, CancellationToken ct) => service.Employees(ct));
api.MapGet("/projects", (TimesheetService service, CancellationToken ct) => service.Projects(ct));
api.MapPost("/periods/close", async (PeriodRequest request, TimesheetService service, CancellationToken ct) => { await service.Close(request.Year, request.Month, true, ct); return Results.NoContent(); });
api.MapPost("/periods/open", async (PeriodRequest request, TimesheetService service, CancellationToken ct) => { await service.Close(request.Year, request.Month, false, ct); return Results.NoContent(); });
api.MapPost("/employees/{id}/rates", (string id, RateUpdateRequest request, TimesheetService service, CancellationToken ct) => service.UpdateRates(id, request, ct));
api.MapPost("/seed", async (TimesheetService service, CancellationToken ct) => { await service.Seed(ct); return Results.NoContent(); });
app.Run();
public sealed record PeriodRequest(int Year, int Month);
public partial class Program;

public sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (DomainException exception)
        {
            context.Response.StatusCode = exception.Status; context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails { Status = exception.Status, Title = exception.Message, Type = $"https://pm-growth.dev/problems/{exception.Code.ToLowerInvariant()}", Instance = context.Request.Path };
            problem.Extensions["code"] = exception.Code; problem.Extensions["details"] = exception.Details; problem.Extensions["traceId"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled error {TraceId}", context.TraceIdentifier); context.Response.StatusCode = 500; context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 500, Title = "Внутренняя ошибка. Обратитесь в поддержку с идентификатором запроса.", Extensions = { ["code"] = "INTERNAL_ERROR", ["traceId"] = context.TraceIdentifier } }, context.RequestAborted);
        }
    }
}
