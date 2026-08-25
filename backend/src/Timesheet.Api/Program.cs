using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.OpenApi.Models;
using Timesheet.Api.Endpoints;
using Timesheet.Api.Middleware;
using Timesheet.Application;
using Timesheet.Application.Validation;
using Timesheet.Domain.Errors;
using Timesheet.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------- Composition ----------

builder.Services.AddScoped<TimesheetService>();
builder.Services.AddValidatorsFromAssemblyContaining<SaveTimeEntryRequestValidator>(ServiceLifetime.Singleton);
builder.Services.AddMongoInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Russian text must not be escaped into \uXXXX: this is about readable logs and curl output.
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Timesheet API",
    Version = "v1",
    Description = "Учёт трудозатрат и стоимости работ по проектам",
}));

builder.Services.AddHealthChecks();

// Responses ASP.NET produces on its own (unmatched route, unparsed body, unsupported
// content type) ship with an empty body by default. This gives them the same contract as
// domain errors: a code, a traceId and a human-readable message.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.HttpContext.Response.StatusCode;

    context.ProblemDetails.Title = StatusTitles.For(status);
    context.ProblemDetails.Extensions["code"] = StatusTitles.CodeFor(status);
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithOrigins("http://localhost:5173", "http://localhost:4173")));

var app = builder.Build();

// ---------- Pipeline ----------

// The error handler goes first: everything thrown by the routes below must pass through it.
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseStatusCodePages();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api");
api.MapTimeEntries();
api.MapReports();
api.MapCatalogs();
api.MapPeriods();
api.MapMaintenance();

app.Run();

/// <summary>Partial so integration tests can boot the host through WebApplicationFactory.</summary>
public partial class Program;

/// <summary>Messages and codes for responses produced by ASP.NET itself rather than the domain.</summary>
internal static class StatusTitles
{
    internal static string For(int status) => status switch
    {
        400 => "Запрос не удалось разобрать: проверьте формат тела и параметров.",
        404 => "Ресурс не найден.",
        405 => "Метод не поддерживается для этого адреса.",
        415 => "Неподдерживаемый тип содержимого: ожидается application/json.",
        _ => "Запрос не выполнен.",
    };

    internal static string CodeFor(int status) => status switch
    {
        400 => ErrorCodes.ValidationFailed,
        404 => "NOT_FOUND",
        405 => "METHOD_NOT_ALLOWED",
        415 => "UNSUPPORTED_MEDIA_TYPE",
        _ => "REQUEST_FAILED",
    };
}
