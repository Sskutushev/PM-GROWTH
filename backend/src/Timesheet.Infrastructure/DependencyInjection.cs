using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Timesheet.Application.Contracts;

namespace Timesheet.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The only place where the application learns that the store is MongoDB.
    /// Swapping the port implementation leaves Application and Domain untouched.
    /// </summary>
    public static IServiceCollection AddMongoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));

        // A silent fallback to localhost hides a broken configuration until the first request
        // and points production at the wrong database. Failing to start is the honest outcome.
        var connectionString = configuration
            .GetSection(MongoOptions.Section)
            .GetValue<string>(nameof(MongoOptions.ConnectionString));

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Не задана строка подключения {MongoOptions.Section}:{nameof(MongoOptions.ConnectionString)}.");
        }

        // MongoClient is thread-safe and owns the connection pool, so it has to be a singleton.
        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        services.AddScoped<ITimesheetStore, MongoTimesheetStore>();
        services.AddHostedService<MongoIndexService>();

        return services;
    }
}
