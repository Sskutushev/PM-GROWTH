using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;
namespace Timesheet.UnitTests;
public sealed class DomainPolicyTests
{
    private static readonly Employee Employee = new() { Id = "e", FullName = "Иванов", Department = "Проектный", Rates = [new(new(2026, 1, 1), 500m), new(new(2026, 3, 1), 600m)] };
    private static readonly Project Project = new() { Id = "p", Code = "П-001", Name = "Цех", Budget = 20_000m, StartDate = new(2026, 1, 1), EndDate = new(2026, 3, 31) };

    [Theory] [InlineData("2026-02-28", 500)] [InlineData("2026-03-01", 600)] [InlineData("2027-01-01", 600)]
    public void Rate_is_resolved_by_entry_date(string value, decimal expected) => Assert.Equal(expected, RateResolver.Resolve(Employee.Rates, DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
    [Fact] public void Missing_rate_has_stable_error_code() => Assert.Equal("RATE_NOT_FOUND", Assert.Throws<DomainException>(() => RateResolver.Resolve(Employee.Rates, new(2025, 12, 31))).Code);
    [Theory] [InlineData(0)] [InlineData(-0.5)] [InlineData(3.7)] [InlineData(24.5)]
    public void Invalid_hours_are_rejected(decimal hours) => Assert.Equal("VALIDATION_FAILED", Assert.Throws<DomainException>(() => WorkHoursPolicy.EnsureEntryHours(hours)).Code);
    [Theory] [InlineData(0.5)] [InlineData(12)] [InlineData(24)] public void Valid_hours_are_accepted(decimal hours) => WorkHoursPolicy.EnsureEntryHours(hours);
    [Fact] public void Daily_limit_is_inclusive() => WorkHoursPolicy.EnsureDailyLimit(20m, 4m);
    [Fact] public void Daily_limit_returns_conflict() => Assert.Equal(409, Assert.Throws<DomainException>(() => WorkHoursPolicy.EnsureDailyLimit(20m, 6m)).Status);
    [Theory] [InlineData(12, false)] [InlineData(12.5, true)] public void Overtime_is_daily(decimal total, bool expected) => Assert.Equal(expected, WorkHoursPolicy.IsOvertime(total));
    [Theory] [InlineData(8, 500, 4000)] [InlineData(8, 600, 4800)] [InlineData(4, 700, 2800)] [InlineData(10, 700, 7000)] [InlineData(8, 650, 5200)]
    public void Acceptance_money_examples_match(decimal hours, decimal rate, decimal amount) => Assert.Equal(amount, Money.Calculate(hours, rate));
    [Fact] public void Money_uses_half_away_from_zero() => Assert.Equal(2.35m, Money.Round(2.345m));
    [Fact] public void Budget_flags_use_unrounded_percentage() { var state = BudgetPolicy.Evaluate(100.004m, 100m); Assert.Equal(100m, state.DisplayPercent); Assert.True(state.IsOverspent); }
    [Fact] public void Zero_budget_has_no_percentage() => Assert.Null(BudgetPolicy.Evaluate(100m, 0m).DisplayPercent);
    [Fact] public void Project_boundaries_are_inclusive() { TimeEntryPolicy.ValidateAndCalculate(false, false, 1m, Project, Employee, Project.StartDate, 0m); TimeEntryPolicy.ValidateAndCalculate(false, false, 1m, Project, Employee, Project.EndDate!.Value, 0m); }
    [Fact] public void Closed_period_precedes_invalid_hours() => Assert.Equal("PERIOD_CLOSED", Assert.Throws<DomainException>(() => TimeEntryPolicy.ValidateAndCalculate(true, false, 3.7m, Project, Employee, new(2026, 2, 1), 0m)).Code);
}
