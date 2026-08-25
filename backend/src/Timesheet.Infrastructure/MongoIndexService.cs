using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Timesheet.Infrastructure;

/// <summary>
/// Creates indexes at startup. The definitions live in <see cref="MongoIndexCatalog"/>, and the
/// operation is idempotent: Mongo does not rebuild an index that already exists with the same
/// name and keys.
/// </summary>
public sealed partial class MongoIndexService(
    IMongoClient client,
    IOptions<MongoOptions> options,
    ILogger<MongoIndexService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await MongoIndexCatalog.EnsureAsync(client.GetDatabase(options.Value.Database), ct);
        LogIndexesEnsured(logger, options.Value.Database);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // Source-generated logging: the message is not formatted when the level is disabled.
    [LoggerMessage(Level = LogLevel.Information, Message = "Mongo indexes ensured for database {Database}")]
    private static partial void LogIndexesEnsured(ILogger logger, string database);
}
