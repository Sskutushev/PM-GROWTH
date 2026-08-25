using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Timesheet.Domain.Errors;

namespace Timesheet.Api.Middleware;

// Единственное место, где исключение превращается в HTTP-ответ.
//   DomainException         — ожидаемая ошибка: 400/404/409 с машиночитаемым кодом;
//   BadHttpRequestException — тело или параметры не разобрались: тоже 400 и тоже с кодом,
//                             иначе клиент получает пустое тело;
//   всё остальное           — дефект сервера: 500, детали в логах, наружу только traceId.
public sealed partial class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception)
        {
            await Write(
                context,
                exception.Status,
                exception.Code,
                exception.Message,
                exception.Details);
        }
        catch (BadHttpRequestException exception)
        {
            LogMalformedRequest(logger, context.Request.Method, context.Request.Path.Value ?? "/", exception.Message);

            await Write(
                context,
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "Запрос не удалось разобрать: проверьте формат тела и параметров.",
                new Dictionary<string, object?> { ["reason"] = exception.Message });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Клиент ушёл — отвечать некому, и это не повод писать в лог 500.
            LogRequestAborted(logger, context.Request.Path.Value ?? "/");
        }
        catch (Exception exception)
        {
            LogUnhandled(
                logger,
                context.TraceIdentifier,
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                exception);

            await Write(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                "Внутренняя ошибка. Обратитесь в поддержку с идентификатором запроса.",
                details: null);
        }
    }

    // Source-generated логирование: аргументы не форматируются, если уровень выключен.
    [LoggerMessage(Level = LogLevel.Information, Message = "Malformed request {Method} {Path}: {Reason}")]
    private static partial void LogMalformedRequest(ILogger logger, string method, string path, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request {Path} aborted by client")]
    private static partial void LogRequestAborted(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error {TraceId} on {Method} {Path}")]
    private static partial void LogUnhandled(
        ILogger logger,
        string traceId,
        string method,
        string path,
        Exception exception);

    private static async Task Write(
        HttpContext context,
        int status,
        string code,
        string title,
        IReadOnlyDictionary<string, object?>? details)
    {
        if (context.Response.HasStarted)
        {
            // Заголовки уже ушли: статус не переписать.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://pm-growth.dev/problems/{code.ToLowerInvariant()}",
            Instance = context.Request.Path,
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (details is { Count: > 0 })
        {
            problem.Extensions["details"] = details;
        }

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, SerializerOptions),
            context.RequestAborted);
    }
}
