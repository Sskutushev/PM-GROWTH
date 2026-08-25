using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Application.Contracts;

namespace Timesheet.Infrastructure;

// The single description of every index. They used to be created only by the hosted service at
// startup, while seeding dropped the collections and their indexes with them. The definitions
// now live here and both startup and the seeder call into them.
internal static class MongoIndexCatalog
{
    internal const string DateProject = "date_project";
    internal const string EmployeeDate = "employee_date";
    internal const string ProjectDate = "project_date";
    internal const string UniqueProjectCode = "unique_code";
    internal const string UniquePeriod = "unique_period";

    /// <summary>Idempotent: Mongo does not rebuild an index with the same name and keys.</summary>
    internal static async Task EnsureAsync(IMongoDatabase database, CancellationToken ct)
    {
        await database
            .GetCollection<BsonDocument>(MongoCollections.TimeEntries)
            .Indexes
            .CreateManyAsync(
                [
                    // Monthly report: a date range first, then grouping by project.
                    Index(Keys.Ascending("date").Ascending("projectId"), DateProject),

                    // Daily cap and the employee+month filter: equality before range (the ESR rule).
                    Index(Keys.Ascending("employeeId").Ascending("date"), EmployeeDate),

                    // The project+month filter.
                    Index(Keys.Ascending("projectId").Ascending("date"), ProjectDate),
                ],
                ct);

        await database
            .GetCollection<BsonDocument>(MongoCollections.Projects)
            .Indexes
            .CreateOneAsync(
                Index(Keys.Ascending("code"), UniqueProjectCode, unique: true),
                cancellationToken: ct);

        await database
            .GetCollection<BsonDocument>(MongoCollections.ClosedPeriods)
            .Indexes
            .CreateOneAsync(
                Index(Keys.Ascending("year").Ascending("month"), UniquePeriod, unique: true),
                cancellationToken: ct);
    }

    internal static async Task<IReadOnlyList<IndexReport>> DescribeAsync(
        IMongoDatabase database,
        CancellationToken ct)
    {
        var reports = new List<IndexReport>(MongoCollections.All.Count);

        foreach (var collection in MongoCollections.All)
        {
            using var cursor = await database
                .GetCollection<BsonDocument>(collection)
                .Indexes
                .ListAsync(ct);

            var indexes = await cursor.ToListAsync(ct);

            reports.Add(new IndexReport(
                collection,
                indexes.Select(x => x["name"].AsString).OrderBy(x => x, StringComparer.Ordinal).ToArray()));
        }

        return reports;
    }

    private static IndexKeysDefinitionBuilder<BsonDocument> Keys => Builders<BsonDocument>.IndexKeys;

    private static CreateIndexModel<BsonDocument> Index(
        IndexKeysDefinition<BsonDocument> keys,
        string name,
        bool unique = false) =>
        new(keys, new CreateIndexOptions { Name = name, Unique = unique });
}
