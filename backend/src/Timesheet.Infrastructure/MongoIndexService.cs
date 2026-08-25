using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Timesheet.Infrastructure;

/// <summary>
/// Создаёт индексы на старте приложения. Операция идемпотентна:
/// Mongo не пересоздаёт индекс, который уже существует с тем же именем и ключами.
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
                    // Отчёт за месяц: диапазон по дате, затем группировка по проекту.
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending("date").Ascending("projectId"),
                        new CreateIndexOptions { Name = "date_project" }),

                    // Суточный лимит и фильтр «сотрудник + месяц»: equality до range (правило ESR).
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending("employeeId").Ascending("date"),
                        new CreateIndexOptions { Name = "employee_date" }),

                    // Фильтр «проект + месяц».
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

    // Source-generated логирование: сообщение не форматируется, если уровень выключен.
    [LoggerMessage(Level = LogLevel.Information, Message = "Mongo indexes ensured for database {Database}")]
    private static partial void LogIndexesEnsured(ILogger logger, string database);
}
