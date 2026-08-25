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

// ---------- Состав приложения ----------

builder.Services.AddScoped<TimesheetService>();
builder.Services.AddValidatorsFromAssemblyContaining<SaveTimeEntryRequestValidator>(ServiceLifetime.Singleton);
builder.Services.AddMongoInfrastructure(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Русский текст в ответах не должен превращаться в \uXXXX: это читаемость логов и curl.
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

// Ответы, которые ASP.NET формирует сам (несовпавший маршрут, неразобранное тело,
// неподдерживаемый Content-Type), по умолчанию уходят с пустым телом.
// Здесь им дописывается тот же контракт, что и у доменных ошибок: код, traceId, русский текст.
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

// ---------- Конвейер ----------

// Обработчик ошибок стоит первым: всё, что бросят маршруты ниже, обязано пройти через него.
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

/// <summary>Точка входа partial, чтобы интеграционные тесты поднимали хост через WebApplicationFactory.</summary>
public partial class Program;

/// <summary>Тексты и коды для ответов, которые формирует сам ASP.NET, а не доменный слой.</summary>
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
