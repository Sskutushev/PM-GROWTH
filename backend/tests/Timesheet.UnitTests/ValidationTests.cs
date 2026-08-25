using Timesheet.Application.Contracts;
using Timesheet.Application.Validation;

namespace Timesheet.UnitTests;

// Валидация входа: запрос синтаксически осмыслен и ошибка доезжает до клиента с именем поля.
public sealed class ValidationTests
{
    private static readonly SaveTimeEntryRequestValidator SaveValidator = new();
    private static readonly TimeEntryQueryValidator QueryValidator = new();
    private static readonly RateUpdateRequestValidator RateValidator = new();
    private static readonly PeriodRequestValidator PeriodValidator = new();

    private static SaveTimeEntryRequest Valid() => new(
        EmployeeId: "ivanov",
        ProjectId: "p001",
        Date: new DateOnly(2026, 3, 5),
        Hours: 8m,
        Comment: "разработка",
        Version: null);

    [Fact]
    public void Valid_request_passes() => Assert.True(SaveValidator.Validate(Valid()).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Employee_is_required(string employeeId)
    {
        var result = SaveValidator.Validate(Valid() with { EmployeeId = employeeId });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(SaveTimeEntryRequest.EmployeeId));
    }

    [Fact]
    public void Project_is_required() =>
        Assert.False(SaveValidator.Validate(Valid() with { ProjectId = "" }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3.7)]
    [InlineData(24.5)]
    public void Hours_outside_the_contract_are_rejected(decimal hours) =>
        Assert.False(SaveValidator.Validate(Valid() with { Hours = hours }).IsValid);

    [Theory]
    [InlineData(0.5)]
    [InlineData(24)]
    public void Hours_on_the_boundary_are_accepted(decimal hours) =>
        Assert.True(SaveValidator.Validate(Valid() with { Hours = hours }).IsValid);

    [Fact]
    public void Comment_length_is_capped() =>
        Assert.False(SaveValidator.Validate(Valid() with { Comment = new string('x', 301) }).IsValid);

    [Fact]
    public void Implausible_year_is_rejected() =>
        Assert.False(SaveValidator.Validate(Valid() with { Date = new DateOnly(1900, 1, 1) }).IsValid);

    [Fact]
    public void Validation_failure_becomes_a_400_with_field_map()
    {
        var result = SaveValidator.Validate(Valid() with { Hours = 3.7m, EmployeeId = "" });
        var error = result.ToDomainException();

        Assert.Equal(400, error.Status);
        Assert.Equal("VALIDATION_FAILED", error.Code);

        var fields = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(error.Details["fields"]);
        Assert.True(fields.ContainsKey("hours"));
        Assert.True(fields.ContainsKey("employeeId"));
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1800, 3)]
    public void Query_month_is_validated(int year, int month) =>
        Assert.False(QueryValidator.Validate(new TimeEntryQuery(year, month, null, null)).IsValid);

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Query_pagination_is_bounded(int page, int pageSize) =>
        Assert.False(QueryValidator.Validate(new TimeEntryQuery(2026, 3, null, null, page, pageSize)).IsValid);

    [Fact]
    public void Query_defaults_are_valid() =>
        Assert.True(QueryValidator.Validate(new TimeEntryQuery(2026, 3, null, null)).IsValid);

    [Fact]
    public void Empty_rate_history_is_rejected() =>
        Assert.False(RateValidator.Validate(new RateUpdateRequest([])).IsValid);

    [Fact]
    public void Non_positive_rate_is_rejected() =>
        Assert.False(RateValidator
            .Validate(new RateUpdateRequest([new RateInput(new DateOnly(2026, 1, 1), -5m)]))
            .IsValid);

    [Fact]
    public void Rate_history_with_positive_values_is_valid() =>
        Assert.True(RateValidator
            .Validate(new RateUpdateRequest([new RateInput(new DateOnly(2026, 1, 1), 500m)]))
            .IsValid);

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(1999, 1)]
    public void Period_request_is_validated(int year, int month) =>
        Assert.False(PeriodValidator.Validate(new PeriodRequest(year, month)).IsValid);
}
