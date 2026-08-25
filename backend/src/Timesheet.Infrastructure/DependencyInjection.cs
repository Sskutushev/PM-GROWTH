using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Timesheet.Application.Contracts;

namespace Timesheet.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Единственная точка, где приложение узнаёт, что хранилище — это MongoDB.
    /// Смена реализации порта не затрагивает Application и Domain.
    /// </summary>
    public static IServiceCollection AddMongoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));

        var connectionString = configuration
            .GetSection(MongoOptions.Section)
            .GetValue<string>(nameof(MongoOptions.ConnectionString))
            ?? "mongodb://localhost:27017";

        // MongoClient потокобезопасен и держит пул соединений — он обязан быть синглтоном.
        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        services.AddScoped<ITimesheetStore, MongoTimesheetStore>();
        services.AddHostedService<MongoIndexService>();

        return services;
    }
}
