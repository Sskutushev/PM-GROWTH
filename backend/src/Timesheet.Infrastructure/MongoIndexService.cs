using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Timesheet.Infrastructure;

/// <summary>
/// Creates indexes at startup. The operation is idempotent: Mongo does not rebuild an index
/// that already exists with the same name and keys.
/// </summary>
public sealed partial class MongoIndexService(
    IMongoClient client,
    IOptions<MongoOptions> options,
    ILogger<MongoIndexService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var database = client.GetDatabase(options.Value.Database);

        await database
            .GetCollection<BsonDocument>(MongoCollections.TimeEntries)
            .Indexes
            .CreateManyAsync(
                [
                    // Monthly report: a date range first, then grouping by project.
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending("date").Ascending("projectId"),
                        new CreateIndexOptions { Name = "date_project" }),

                    // Daily cap and the employee+month filter: equality before range (the ESR rule).
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending("employeeId").Ascending("date"),
                        new CreateIndexOptions { Name = "employee_date" }),

                    // The project+month filter.
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending("projectId").Ascending("date"),
                        new CreateIndexOptions { Name = "project_date" }),
                ],
                ct);

        await database
            .GetCollection<BsonDocument>(MongoCollections.Projects)
            .Indexes
            .CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("code"),
                    new CreateIndexOptions { Unique = true, Name = "unique_code" }),
                cancellationToken: ct);

        await database
            .GetCollection<BsonDocument>(MongoCollections.ClosedPeriods)
            .Indexes
            .CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("year").Ascending("month"),
                    new CreateIndexOptions { Unique = true, Name = "unique_period" }),
                cancellationToken: ct);

        LogIndexesEnsured(logger, options.Value.Database);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // Source-generated logging: the message is not formatted when the level is disabled.
    [LoggerMessage(Level = LogLevel.Information, Message = "Mongo indexes ensured for database {Database}")]
    private static partial void LogIndexesEnsured(ILogger logger, string database);
}
