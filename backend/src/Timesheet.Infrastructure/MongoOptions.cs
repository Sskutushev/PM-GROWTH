namespace Timesheet.Infrastructure;

public sealed class MongoOptions
{
    public const string Section = "Mongo";

    public string ConnectionString { get; init; } = "mongodb://localhost:27017";

    public string Database { get; init; } = "timesheet";
}
