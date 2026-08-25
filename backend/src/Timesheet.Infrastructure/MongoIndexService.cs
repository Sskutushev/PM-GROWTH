using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
namespace Timesheet.Infrastructure;
public sealed class MongoIndexService(IMongoClient client, IOptions<MongoOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var db = client.GetDatabase(options.Value.Database); var entries = db.GetCollection<BsonDocument>("time_entries");
        await entries.Indexes.CreateManyAsync([
            new(Builders<BsonDocument>.IndexKeys.Ascending("date").Ascending("projectId"), new CreateIndexOptions { Name = "date_project" }),
            new(Builders<BsonDocument>.IndexKeys.Ascending("employeeId").Ascending("date"), new CreateIndexOptions { Name = "employee_date" }),
            new(Builders<BsonDocument>.IndexKeys.Ascending("projectId").Ascending("date"), new CreateIndexOptions { Name = "project_date" })], ct);
        await db.GetCollection<BsonDocument>("projects").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("code"), new CreateIndexOptions { Unique = true, Name = "unique_code" }), cancellationToken: ct);
        await db.GetCollection<BsonDocument>("closed_periods").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("year").Ascending("month"), new CreateIndexOptions { Unique = true, Name = "unique_period" }), cancellationToken: ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
