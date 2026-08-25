using MongoDB.Bson;
using MongoDB.Driver;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;

namespace Timesheet.Infrastructure;

/// <summary>
/// Enforces the 24-hour daily cap across documents.
/// <para>
/// Checking the cap by reading the entries and then inserting is a read-check-write: two
/// concurrent requests both read 8 hours, both accept 10 more, and the day ends at 28. A Mongo
/// transaction alone does not close that hole either — snapshot isolation stops conflicting
/// writes to the same document, not an insert of a new one.
/// </para>
/// <para>
/// So every write of a day passes through one document, <c>daily_hours</c>, keyed by employee
/// and date. The reservation is a conditional <c>$inc</c>: it only applies while the day still
/// has room, and two concurrent transactions touching the same document produce a write
/// conflict, which serialises them. The document is derived state — <c>time_entries</c> stays
/// the source of truth, and seeding rebuilds it.
/// </para>
/// </summary>
internal static class MongoDailyHoursGuard
{
    /// <summary>Raised when the conditional increment finds no room left in the day.</summary>
    internal sealed class DayIsFullException : Exception;

    internal static async Task Reserve(
        IMongoDatabase database,
        IClientSessionHandle session,
        string employeeId,
        DateOnly date,
        decimal hours,
        CancellationToken ct)
    {
        // The filter is the whole guarantee: no room, no match, and the upsert then collides
        // with the existing _id instead of silently creating a second document.
        var filter = Builders<BsonDocument>.Filter.Eq("_id", Key(employeeId, date))
            & Builders<BsonDocument>.Filter.Lte(
                "totalHours",
                MongoMapping.Decimal(WorkHoursPolicy.DailyLimit - hours));

        var update = Builders<BsonDocument>.Update
            .Inc("totalHours", MongoMapping.Decimal(hours))
            .SetOnInsert("employeeId", employeeId)
            .SetOnInsert("date", MongoMapping.ToUtc(date));

        try
        {
            await Collection(database).UpdateOneAsync(
                session,
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DayIsFullException();
        }
    }

    internal static Task Release(
        IMongoDatabase database,
        IClientSessionHandle session,
        string employeeId,
        DateOnly date,
        decimal hours,
        CancellationToken ct) =>
        Collection(database).UpdateOneAsync(
            session,
            Builders<BsonDocument>.Filter.Eq("_id", Key(employeeId, date)),
            Builders<BsonDocument>.Update.Inc("totalHours", MongoMapping.Decimal(-hours)),
            cancellationToken: ct);

    /// <summary>
    /// Recomputes every daily total from the entries. Used by seeding, and the operation that
    /// would repair the guard if it ever drifted from the entries.
    /// </summary>
    internal static async Task Rebuild(IMongoDatabase database, CancellationToken ct)
    {
        var totals = await database
            .GetCollection<BsonDocument>(MongoCollections.TimeEntries)
            .Aggregate()
            .Group(new BsonDocument
            {
                ["_id"] = new BsonDocument { ["employeeId"] = "$employeeId", ["date"] = "$date" },
                ["totalHours"] = new BsonDocument("$sum", "$hours"),
            })
            .ToListAsync(ct);

        var collection = Collection(database);
        await collection.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct);

        if (totals.Count == 0)
        {
            return;
        }

        var documents = totals.Select(row =>
        {
            var key = row["_id"].AsBsonDocument;
            var employeeId = key["employeeId"].AsString;
            var date = MongoMapping.ToDate(key["date"]);

            return new BsonDocument
            {
                ["_id"] = Key(employeeId, date),
                ["employeeId"] = employeeId,
                ["date"] = MongoMapping.ToUtc(date),
                ["totalHours"] = row["totalHours"],
            };
        });

        await collection.InsertManyAsync(documents, cancellationToken: ct);
    }

    /// <summary>Daily totals as the guard sees them, for diagnostics and for the drift test.</summary>
    internal static async Task<IReadOnlyDictionary<(string EmployeeId, DateOnly Date), decimal>> Snapshot(
        IMongoDatabase database,
        CancellationToken ct)
    {
        var documents = await Collection(database)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(ct);

        return documents.ToDictionary(
            x => (x["employeeId"].AsString, MongoMapping.ToDate(x["date"])),
            x => MongoMapping.Decimal(x["totalHours"]));
    }

    internal static Task Reserve(
        IMongoDatabase database,
        IClientSessionHandle session,
        TimeEntry entry,
        CancellationToken ct) =>
        Reserve(database, session, entry.EmployeeId, entry.Date, entry.Hours, ct);

    internal static Task Release(
        IMongoDatabase database,
        IClientSessionHandle session,
        TimeEntry entry,
        CancellationToken ct) =>
        Release(database, session, entry.EmployeeId, entry.Date, entry.Hours, ct);

    private static IMongoCollection<BsonDocument> Collection(IMongoDatabase database) =>
        database.GetCollection<BsonDocument>(MongoCollections.DailyHours);

    private static string Key(string employeeId, DateOnly date) => $"{employeeId}|{date:yyyy-MM-dd}";
}
