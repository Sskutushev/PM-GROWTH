using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Timesheet.Application.Contracts;
namespace Timesheet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMongoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));
        var connection = configuration.GetSection(MongoOptions.Section).GetValue<string>(nameof(MongoOptions.ConnectionString)) ?? "mongodb://localhost:27017";
        services.AddSingleton<IMongoClient>(_ => new MongoClient(connection)); services.AddScoped<ITimesheetStore, MongoTimesheetStore>(); services.AddHostedService<MongoIndexService>();
        return services;
    }
}
