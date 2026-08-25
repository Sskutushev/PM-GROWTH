using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.OpenApi.Models;
using Timesheet.Api.Endpoints;
using Timesheet.Api.Middleware;
using Timesheet.Application;
using Timesheet.Application.Validation;
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

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithOrigins("http://localhost:5173", "http://localhost:4173")));

var app = builder.Build();

// ---------- Конвейер ----------

// Обработчик ошибок стоит первым: всё, что бросят маршруты ниже, обязано пройти через него.
app.UseMiddleware<ProblemDetailsMiddleware>();
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

/// <summary>Точка входа объявлена partial, чтобы интеграционные тесты могли поднять хост через WebApplicationFactory.</summary>
public partial class Program;
