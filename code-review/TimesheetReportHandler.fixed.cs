// Corrected version of TimesheetReportHandler.cs. The numbers in the comments point at the
// rows of REVIEW.md, so every fix can be traced back to the defect it answers.
//
// The shape of the fix: the handler owns the use case and nothing else, the database does the
// grouping, money is decimal everywhere, and every failure the caller can act on is a typed
// error instead of a 500. Production code lives in backend/; this file is the review answer.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Demo.Api.Queries.Reports;

// (21) Immutable DTO: nothing can rewrite a row after it is built.
// (6) decimal for money. (8) Percent is nullable: a zero budget has no percentage.
public sealed record ProjectReportRow(
    string ProjectId,
    string ProjectName,
    decimal Hours,
    decimal Amount,
    decimal Budget,
    decimal? Percent,
    bool Overspent);

public sealed record GetProjectReportQuery(int Year, int Month);

// (19) The use case depends on a port, not on IMongoDatabase, so it is unit-testable without
// a database. (17) Collection names live with the adapter, not in the use case.
public interface IProjectReportSource
{
    Task<IReadOnlyList<ProjectAggregate>> Aggregate(MonthRange range, CancellationToken ct);

    Task<IReadOnlyDictionary<string, ProjectInfo>> Projects(
        IReadOnlyCollection<string> ids,
        CancellationToken ct);
}

public sealed record ProjectAggregate(string ProjectId, decimal Hours, decimal Amount);

public sealed record ProjectInfo(string Id, string Name, decimal Budget);

// (12) The contract is validated before any work starts, so month=13 is a 400 and not an
// empty report. (10) A month is a half-open range of dates, never a Year/Month comparison.
public readonly record struct MonthRange(DateOnly Start, DateOnly EndExclusive)
{
    public static MonthRange Create(int year, int month)
    {
        if (year is < 2000 or > 2100)
        {
            throw new ReportContractException(nameof(year), "Год вне допустимого диапазона.");
        }

        if (month is < 1 or > 12)
        {
            throw new ReportContractException(nameof(month), "Месяц должен быть от 1 до 12.");
        }

        var start = new DateOnly(year, month, 1);
        return new MonthRange(start, start.AddMonths(1));
    }
}

public sealed class ReportContractException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class BrokenReferenceException(string projectId)
    : Exception($"Проект {projectId} отсутствует в справочнике.")
{
    public string ProjectId { get; } = projectId;
}

// (7, 13, 14) One rounding rule for the whole application: half away from zero, applied per
// entry, and the flags are computed from the unrounded percent so 100.004% still counts as an
// overspend.
public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal? Percent(decimal amount, decimal budget) =>
        budget <= 0m ? null : amount / budget * 100m;
}

public sealed partial class TimesheetReportHandler(
    IProjectReportSource source,
    ILogger<TimesheetReportHandler> logger)
{
    public async Task<IReadOnlyList<ProjectReportRow>> Handle(
        GetProjectReportQuery request,
        CancellationToken ct)
    {
        var range = MonthRange.Create(request.Year, request.Month);
        var started = Stopwatch.GetTimestamp();

        // (1, 2, 3, 15) One aggregation over an indexed date range returns one row per project.
        // (11) The cancellation token reaches the driver, so an abandoned request stops working.
        var aggregates = await source.Aggregate(range, ct);

        // (3) The catalogue is read once, by the ids the report actually references.
        var projects = await source.Projects(aggregates.Select(x => x.ProjectId).ToArray(), ct);

        var rows = new List<ProjectReportRow>(aggregates.Count);

        foreach (var aggregate in aggregates)
        {
            // (9, 20) A missing project is a named error with the id in it, not a
            // NullReferenceException, and the dictionary is probed once.
            if (!projects.TryGetValue(aggregate.ProjectId, out var project))
            {
                throw new BrokenReferenceException(aggregate.ProjectId);
            }

            var percent = Money.Percent(aggregate.Amount, project.Budget);

            rows.Add(new ProjectReportRow(
                project.Id,
                project.Name,
                aggregate.Hours,
                aggregate.Amount,
                project.Budget,
                percent is null ? null : Money.Round(percent.Value),
                percent > 100m));
        }

        // (22) Duration and row count are logged, so a regression is visible before users report it.
        LogReportBuilt(
            logger,
            request.Year,
            request.Month,
            rows.Count,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return rows.OrderBy(x => x.ProjectName, StringComparer.CurrentCulture).ToArray();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Project report {Year}-{Month} built: {Rows} rows in {Elapsed} ms")]
    private static partial void LogReportBuilt(
        ILogger logger,
        int year,
        int month,
        int rows,
        double elapsed);
}

// (18) Persistence lives in its own type. (5) The rate is resolved when the entry is written,
// so the report sums a stored amount instead of guessing a rate afterwards; the resolver below
// is the rule that write path uses.
public sealed class MongoProjectReportSource(IMongoDatabase database) : IProjectReportSource
{
    private const string Entries = "time_entries";
    private const string Projects_ = "projects";

    public async Task<IReadOnlyList<ProjectAggregate>> Aggregate(MonthRange range, CancellationToken ct)
    {
        // (16) Served by { date: 1, projectId: 1 }: the range comes first, then the grouping key.
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("date", new BsonDocument
            {
                ["$gte"] = ToUtc(range.Start),
                ["$lt"] = ToUtc(range.EndExclusive),
            })),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$projectId",
                ["hours"] = new BsonDocument("$sum", "$hours"),
                ["amount"] = new BsonDocument("$sum", "$amount"),
            }),
        };

        var documents = await database
            .GetCollection<BsonDocument>(Entries)
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync(ct);

        return documents
            .Select(x => new ProjectAggregate(
                x["_id"].AsString,
                Decimal(x["hours"]),
                Decimal(x["amount"])))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, ProjectInfo>> Projects(
        IReadOnlyCollection<string> ids,
        CancellationToken ct)
    {
        var documents = await database
            .GetCollection<BsonDocument>(Projects_)
            .Find(Builders<BsonDocument>.Filter.In<string>("_id", ids))
            .ToListAsync(ct);

        return documents.ToDictionary(
            x => x["_id"].AsString,
            x => new ProjectInfo(x["_id"].AsString, x["name"].AsString, Decimal(x["budget"])));
    }

    // (10) Dates are stored as UTC midnight, so a local evening never lands in another month.
    private static DateTime ToUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    // (6) Decimal128 in the database maps to decimal in C#; double never touches money.
    private static decimal Decimal(BsonValue value) => value.IsDecimal128
        ? MongoDB.Bson.Decimal128.ToDecimal(value.AsDecimal128)
        : throw new InvalidOperationException("Денежное поле должно храниться как Decimal128.");
}

// (5) The rate that applies on a date is the latest one starting on or before it — not the
// first element of the array. An entry earlier than the whole history is an explicit error.
public static class RateResolver
{
    public static decimal Resolve(IReadOnlyList<(DateOnly From, decimal Value)> history, DateOnly date)
    {
        var applicable = history
            .Where(x => x.From <= date)
            .OrderByDescending(x => x.From)
            .Select(x => (decimal?)x.Value)
            .FirstOrDefault();

        return applicable
            ?? throw new InvalidOperationException($"На {date:yyyy-MM-dd} нет действующей ставки.");
    }
}
