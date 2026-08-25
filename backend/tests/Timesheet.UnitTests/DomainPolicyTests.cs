using System.Globalization;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;

namespace Timesheet.UnitTests;

// The domain depends on neither Mongo nor ASP.NET, so there are no mocks and no containers here.
public sealed class DomainPolicyTests
{
    private static readonly Employee Ivanov = new()
    {
        Id = "ivanov",
        FullName = "Иванов И. И.",
        Department = "Проектный",
        Rates =
        [
            new HourlyRate(new DateOnly(2026, 1, 1), 500m),
            new HourlyRate(new DateOnly(2026, 3, 1), 600m),
        ],
    };

    private static readonly Project Workshop = new()
    {
        Id = "p001",
        Code = "П-001",
        Name = "Реконструкция цеха",
        Budget = 20_000m,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 3, 31),
    };

    private static readonly Project Endless = new()
    {
        Id = "p002",
        Code = "П-002",
        Name = "Инженерные сети",
        Budget = 5_000m,
        StartDate = new DateOnly(2026, 3, 1),
        EndDate = null,
    };

    private static DateOnly D(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------- Rate selection by date ----------

    [Theory]
    [InlineData("2026-01-01", 500)] // first day the first rate is in force
    [InlineData("2026-02-28", 500)] // the day before the raise
    [InlineData("2026-03-01", 600)] // the day of the raise: the new rate applies immediately
    [InlineData("2027-01-01", 600)] // the last rate stays in force indefinitely
    public void Rate_is_resolved_by_entry_date(string date, decimal expected) =>
        Assert.Equal(expected, RateResolver.Resolve(Ivanov.Rates, D(date)));

    [Fact]
    public void Rate_before_first_record_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => RateResolver.Resolve(Ivanov.Rates, D("2025-12-31")));

        Assert.Equal(ErrorCodes.RateNotFound, error.Code);
        Assert.Equal(400, error.Status);
        Assert.Contains("31.12.2025", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rate_resolution_ignores_order_of_history()
    {
        var shuffled = new[]
        {
            new HourlyRate(new DateOnly(2026, 3, 1), 600m),
            new HourlyRate(new DateOnly(2026, 1, 1), 500m),
        };

        Assert.Equal(600m, RateResolver.Resolve(shuffled, D("2026-03-05")));
    }

    [Fact]
    public void Duplicate_effective_dates_make_rate_ambiguous()
    {
        var broken = new[]
        {
            new HourlyRate(new DateOnly(2026, 1, 1), 500m),
            new HourlyRate(new DateOnly(2026, 1, 1), 700m),
        };

        Assert.Equal(
            ErrorCodes.RateHistoryInvalid,
            Assert.Throws<DomainException>(() => RateResolver.Resolve(broken, D("2026-02-01"))).Code);
    }

    [Fact]
    public void Try_resolve_reports_missing_rate_without_throwing()
    {
        Assert.False(RateResolver.TryResolve(Ivanov.Rates, D("2025-01-01"), out var rate));
        Assert.Equal(0m, rate);
    }

    [Fact]
    public void Empty_rate_history_is_rejected_before_saving() =>
        Assert.Equal(
            ErrorCodes.ValidationFailed,
            Assert.Throws<DomainException>(() => RateResolver.EnsureHistoryIsValid([])).Code);

    [Fact]
    public void Non_positive_rate_is_rejected_before_saving() =>
        Assert.Equal(
            ErrorCodes.ValidationFailed,
            Assert.Throws<DomainException>(() =>
                RateResolver.EnsureHistoryIsValid([new HourlyRate(D("2026-01-01"), 0m)])).Code);

    // ---------- Hours ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(3.7)]
    [InlineData(24.5)]
    [InlineData(0.25)]
    public void Invalid_hours_are_rejected(decimal hours) =>
        Assert.Equal(
            ErrorCodes.ValidationFailed,
            Assert.Throws<DomainException>(() => WorkHoursPolicy.EnsureEntryHours(hours)).Code);

    [Theory]
    [InlineData(0.5)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public void Valid_hours_are_accepted(decimal hours) => WorkHoursPolicy.EnsureEntryHours(hours);

    [Fact]
    public void Invalid_hours_error_names_the_field()
    {
        var error = Assert.Throws<DomainException>(() => WorkHoursPolicy.EnsureEntryHours(3.7m));
        Assert.Equal("hours", error.Details["field"]);
    }

    [Fact]
    public void Daily_limit_boundary_is_inclusive() => WorkHoursPolicy.EnsureDailyLimit(20m, 4m);

    [Fact]
    public void Daily_limit_excess_is_a_conflict()
    {
        var error = Assert.Throws<DomainException>(() => WorkHoursPolicy.EnsureDailyLimit(20m, 6m));

        Assert.Equal(ErrorCodes.DailyHoursExceeded, error.Code);
        Assert.Equal(409, error.Status);
        Assert.Contains("26", error.Message, StringComparison.Ordinal);
        Assert.Equal(20m, error.Details["alreadyLogged"]);
        Assert.Equal(6m, error.Details["requested"]);
    }

    [Theory]
    [InlineData(12, false)] // exactly at the threshold is not yet overtime
    [InlineData(12.5, true)]
    [InlineData(20, true)]
    public void Overtime_is_a_property_of_the_day(decimal dailyHours, bool expected) =>
        Assert.Equal(expected, WorkHoursPolicy.IsOvertime(dailyHours));

    // ---------- Money ----------

    [Theory]
    [InlineData(8, 500, 4000)]
    [InlineData(8, 600, 4800)]
    [InlineData(4, 700, 2800)]
    [InlineData(10, 700, 7000)]
    [InlineData(8, 650, 5200)] // acceptance scenario 8
    public void Acceptance_money_examples_match(decimal hours, decimal rate, decimal expected) =>
        Assert.Equal(expected, Money.Calculate(hours, rate));

    [Theory]
    [InlineData(2.345, 2.35)] // halves go away from zero, not to even
    [InlineData(2.355, 2.36)]
    [InlineData(-2.345, -2.35)]
    [InlineData(0.005, 0.01)]
    public void Money_rounds_half_away_from_zero(decimal value, decimal expected) =>
        Assert.Equal(expected, Money.Round(value));

    [Fact]
    public void Money_keeps_precision_where_double_would_drift()
    {
        // 0.1 + 0.2 in double gives 0.30000000000000004; decimal accumulates no such error.
        var total = Enumerable.Range(0, 10).Aggregate(0m, (sum, _) => sum + Money.Calculate(0.5m, 33.33m));
        Assert.Equal(166.70m, total);
    }

    // ---------- Budget ----------

    [Theory]
    [InlineData(7600, 20000, 38)]
    [InlineData(7000, 5000, 140)]
    [InlineData(4000, 20000, 20)]
    public void Budget_percentage_matches_acceptance_table(decimal amount, decimal budget, decimal expected) =>
        Assert.Equal(expected, BudgetPolicy.Evaluate(amount, budget).DisplayPercent);

    [Fact]
    public void Budget_flags_use_unrounded_percentage()
    {
        // 100.004% rounds to 100.00%, but the overspend must stay visible.
        var state = BudgetPolicy.Evaluate(100.004m, 100m);

        Assert.Equal(100m, state.DisplayPercent);
        Assert.True(state.IsOverspent);
        Assert.False(state.IsAtRisk);
    }

    [Theory]
    [InlineData(80, false, false)] // exactly at the risk threshold is not yet at risk
    [InlineData(80.01, true, false)]
    [InlineData(100, true, false)] // exactly on budget is at risk but not overspent
    [InlineData(100.01, false, true)]
    public void Risk_and_overspend_do_not_overlap(decimal amount, bool atRisk, bool overspent)
    {
        var state = BudgetPolicy.Evaluate(amount, 100m);

        Assert.Equal(atRisk, state.IsAtRisk);
        Assert.Equal(overspent, state.IsOverspent);
    }

    [Fact]
    public void Zero_budget_has_no_percentage()
    {
        var state = BudgetPolicy.Evaluate(100m, 0m);

        Assert.Null(state.DisplayPercent);
        Assert.Null(state.RawPercent);
        Assert.False(state.IsOverspent);
    }

    // ---------- Month ----------

    [Fact]
    public void Month_is_a_half_open_interval()
    {
        var february = MonthRange.Create(2026, 2);

        Assert.Equal(D("2026-02-01"), february.Start);
        Assert.Equal(D("2026-03-01"), february.EndExclusive);
        Assert.True(february.Contains(D("2026-02-28")));
        Assert.False(february.Contains(D("2026-03-01")));
    }

    [Fact]
    public void December_rolls_over_to_the_next_year() =>
        Assert.Equal(D("2027-01-01"), MonthRange.Create(2026, 12).EndExclusive);

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1800, 3)]
    [InlineData(9999, 3)]
    public void Invalid_month_is_a_client_error_not_a_server_fault(int year, int month)
    {
        // The exception type is a domain one, which is what makes the API answer 400 and not 500.
        var error = Assert.Throws<DomainException>(() => MonthRange.Create(year, month));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
    }

    // ---------- Entry rules ----------

    [Fact]
    public void Project_boundaries_are_inclusive()
    {
        TimeEntryPolicy.EnsureDateWithinProject(Workshop, Workshop.StartDate);
        TimeEntryPolicy.EnsureDateWithinProject(Workshop, Workshop.EndDate!.Value);
    }

    [Theory]
    [InlineData("2025-12-31")]
    [InlineData("2026-04-01")]
    public void Dates_outside_the_project_are_rejected(string date)
    {
        var error = Assert.Throws<DomainException>(() => TimeEntryPolicy.EnsureDateWithinProject(Workshop, D(date)));

        Assert.Equal(ErrorCodes.DateOutsideProjectPeriod, error.Code);
        Assert.Equal("П-001", error.Details["projectCode"]);
    }

    [Fact]
    public void Open_ended_project_accepts_any_date_after_start()
    {
        TimeEntryPolicy.EnsureDateWithinProject(Endless, D("2099-12-31"));

        Assert.Equal(
            ErrorCodes.DateOutsideProjectPeriod,
            Assert.Throws<DomainException>(() => TimeEntryPolicy.EnsureDateWithinProject(Endless, D("2026-02-28"))).Code);
    }

    [Fact]
    public void Closed_period_is_reported_before_any_other_problem()
    {
        var context = Context(hours: 3.7m, date: D("2026-02-01"), sourceClosed: true);

        Assert.Equal(
            ErrorCodes.PeriodClosed,
            Assert.Throws<DomainException>(() => TimeEntryPolicy.ValidateAndResolveRate(context)).Code);
    }

    [Fact]
    public void Hours_are_checked_before_the_daily_limit()
    {
        var context = Context(hours: 3.7m, date: D("2026-03-05"), alreadyLogged: 23m);

        Assert.Equal(
            ErrorCodes.ValidationFailed,
            Assert.Throws<DomainException>(() => TimeEntryPolicy.ValidateAndResolveRate(context)).Code);
    }

    [Fact]
    public void Missing_rate_is_reported_before_the_daily_limit()
    {
        var context = Context(hours: 8m, date: D("2026-01-05"), alreadyLogged: 23m) with
        {
            Employee = new Employee
            {
                Id = "petrova",
                FullName = "Петрова А. С.",
                Department = "Проектный",
                Rates = [new HourlyRate(new DateOnly(2026, 2, 1), 700m)],
            },
        };

        Assert.Equal(
            ErrorCodes.RateNotFound,
            Assert.Throws<DomainException>(() => TimeEntryPolicy.ValidateAndResolveRate(context)).Code);
    }

    [Fact]
    public void Valid_entry_returns_the_effective_rate() =>
        Assert.Equal(600m, TimeEntryPolicy.ValidateAndResolveRate(Context(hours: 8m, date: D("2026-03-05"))));

    [Fact]
    public void Moving_an_entry_out_of_a_closed_month_is_rejected()
    {
        var context = Context(hours: 8m, date: D("2026-03-05"), sourceClosed: true);

        Assert.Equal(
            ErrorCodes.PeriodClosed,
            Assert.Throws<DomainException>(() => TimeEntryPolicy.ValidateAndResolveRate(context)).Code);
    }

    private static TimeEntryContext Context(
        decimal hours,
        DateOnly date,
        bool sourceClosed = false,
        bool targetClosed = false,
        decimal alreadyLogged = 0m) =>
        new(
            Employee: Ivanov,
            Project: Workshop,
            Date: date,
            Hours: hours,
            SourcePeriodClosed: sourceClosed,
            TargetPeriodClosed: targetClosed,
            HoursAlreadyLoggedThatDay: alreadyLogged);
}
