using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Timesheet.IntegrationTests;

// Один контейнер с MongoDB и один хост на весь прогон: поднимать их на каждый класс
// тестов дорого. Изоляция между тестами обеспечивается пересевом данных.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MongoDbContainer mongo = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .Build();

    private WebApplicationFactory<Program>? factory;

    public HttpClient Client { get; private set; } = null!;

    public IMongoDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await mongo.StartAsync();

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Mongo:ConnectionString", mongo.GetConnectionString()));

        Client = factory.CreateClient();
        Database = new MongoClient(mongo.GetConnectionString()).GetDatabase("timesheet");
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await mongo.DisposeAsync();
    }

    /// <summary>Возвращает базу к контрольным данным задания перед каждым тестом.</summary>
    public async Task ResetAsync()
    {
        var response = await Client.PostAsync("/api/seed", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> IndexNames(string collection)
    {
        using var cursor = await Database.GetCollection<BsonDocument>(collection).Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        return indexes.Select(x => x["name"].AsString).ToArray();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
