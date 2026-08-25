using Timesheet.Application;
using Timesheet.Application.Contracts;
using Timesheet.Application.Validation;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;

namespace Timesheet.UnitTests;

// Application-layer scenarios against the in-memory store: context loading order, moving an
// entry between months, optimistic concurrency and which error code reaches the client.
public sealed class TimesheetServiceTests
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

    private static readonly Employee Petrova = new()
    {
        Id = "petrova",
        FullName = "Петрова А. С.",
        Department = "Проектный",
        Rates = [new HourlyRate(new DateOnly(2026, 2, 1), 700m)],
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

    private static readonly Project Networks = new()
    {
        Id = "p002",
        Code = "П-002",
        Name = "Инженерные сети",
        Budget = 5_000m,
        StartDate = new DateOnly(2026, 3, 1),
        EndDate = null,
    };

    private static (TimesheetService Service, InMemoryTimesheetStore Store) Build()
    {
        var store = new InMemoryTimesheetStore()
            .WithEmployee(Ivanov)
            .WithEmployee(Petrova)
            .WithProject(Workshop)
            .WithProject(Networks);

        var service = new TimesheetService(
            store,
            new SaveTimeEntryRequestValidator(),
            new TimeEntryQueryValidator(),
            new RateUpdateRequestValidator());

        return (service, store);
    }

    private static SaveTimeEntryRequest Request(
        string employeeId = "ivanov",
        string projectId = "p001",
        string date = "2026-03-05",
        decimal hours = 8m,
        long? version = null) =>
        new(
            employeeId,
            projectId,
            DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            hours,
            string.Empty,
            version);

    // ---------- Creation ----------

    [Fact]
    public async Task Create_applies_the_rate_effective_on_the_entry_date()
    {
        var (service, _) = Build();

        var entry = await service.Create(Request(date: "2026-03-05"), default);

        Assert.Equal(600m, entry.AppliedRate);
        Assert.Equal(4_800m, entry.Amount);
        Assert.Equal(1, entry.Version);
    }

    [Fact]
    public async Task Create_uses_the_old_rate_before_the_increase()
    {
        var (service, _) = Build();

        var entry = await service.Create(Request(date: "2026-02-20"), default);

        Assert.Equal(500m, entry.AppliedRate);
        Assert.Equal(4_000m, entry.Amount);
    }

    [Fact]
    public async Task Create_is_rejected_when_the_employee_has_no_rate_yet()
    {
        var (service, store) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Create(Request(employeeId: "petrova", date: "2026-01-15", hours: 1m), default));

        Assert.Equal(ErrorCodes.RateNotFound, error.Code);
        Assert.Equal(0, store.EntryCount);
    }

    [Fact]
    public async Task Create_is_rejected_before_the_project_starts()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Create(Request(projectId: "p002", date: "2026-02-20", hours: 1m), default));

        Assert.Equal(ErrorCodes.DateOutsideProjectPeriod, error.Code);
    }

    [Fact]
    public async Task Create_is_rejected_for_an_unknown_employee()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Create(Request(employeeId: "ghost"), default));

        Assert.Equal(ErrorCodes.EmployeeNotFound, error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task Create_is_rejected_for_an_unknown_project()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Create(Request(projectId: "ghost"), default));

        Assert.Equal(ErrorCodes.ProjectNotFound, error.Code);
    }

    [Fact]
    public async Task Create_is_rejected_in_a_closed_period()
    {
        var (service, store) = Build();
        store.WithClosedPeriod(2026, 3);

        var error = await Assert.ThrowsAsync<DomainException>(() => service.Create(Request(), default));

        Assert.Equal(ErrorCodes.PeriodClosed, error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Invalid_hours_never_reach_the_store()
    {
        var (service, store) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() => service.Create(Request(hours: 3.7m), default));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(0, store.EntryCount);
    }

    // ---------- Daily cap ----------

    [Fact]
    public async Task Twenty_hours_are_saved_and_marked_as_overtime()
    {
        var (service, _) = Build();

        await service.Create(Request(date: "2026-03-06", hours: 20m), default);

        var page = await service.List(new TimeEntryQuery(2026, 3, null, null), default);

        Assert.Single(page.Items);
        Assert.True(page.Items[0].IsOvertime);
        Assert.Equal(20m, page.Items[0].DailyHours);
    }

    [Fact]
    public async Task Twenty_six_hours_in_a_day_are_rejected_across_projects()
    {
        var (service, store) = Build();

        await service.Create(Request(date: "2026-03-06", hours: 20m), default);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Create(Request(projectId: "p002", date: "2026-03-06", hours: 6m), default));

        Assert.Equal(ErrorCodes.DailyHoursExceeded, error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(1, store.EntryCount);
    }

    [Fact]
    public async Task Exactly_twenty_four_hours_are_allowed()
    {
        var (service, _) = Build();

        await service.Create(Request(date: "2026-03-06", hours: 20m), default);
        await service.Create(Request(projectId: "p002", date: "2026-03-06", hours: 4m), default);

        var page = await service.List(new TimeEntryQuery(2026, 3, null, null), default);
        Assert.Equal(24m, page.TotalHours);
    }

    [Fact]
    public async Task Editing_an_entry_does_not_count_it_against_itself()
    {
        var (service, _) = Build();

        var created = await service.Create(Request(date: "2026-03-06", hours: 20m), default);

        // Same day, 24 hours: if the previous 20 still counted this would be 44 and get rejected.
        var updated = await service.Update(
            created.Id,
            Request(date: "2026-03-06", hours: 24m, version: created.Version),
            default);

        Assert.Equal(24m, updated.Hours);
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task Second_save_of_a_stale_version_is_rejected()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(), default);

        // The first tab saved its changes.
        await service.Update(created.Id, Request(hours: 4m, version: created.Version), default);

        // The second tab still holds the version it saw when the form was opened.
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Update(created.Id, Request(hours: 6m, version: created.Version), default));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(4m, store.Stored(created.Id).Hours);
    }

    [Fact]
    public async Task Update_without_a_version_is_rejected()
    {
        var (service, _) = Build();
        var created = await service.Create(Request(), default);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Update(created.Id, Request(version: null), default));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public async Task Version_increases_on_every_successful_save()
    {
        var (service, _) = Build();
        var created = await service.Create(Request(), default);

        var updated = await service.Update(created.Id, Request(hours: 4m, version: created.Version), default);

        Assert.Equal(created.Version + 1, updated.Version);
    }

    [Fact]
    public async Task Delete_with_a_stale_version_is_rejected()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(), default);
        store.BumpVersionOutOfBand(created.Id);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Delete(created.Id, created.Version, default));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Equal(1, store.EntryCount);
    }

    [Fact]
    public async Task Delete_removes_the_entry()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(), default);

        await service.Delete(created.Id, created.Version, default);

        Assert.Equal(0, store.EntryCount);
    }

    [Fact]
    public async Task Delete_of_a_missing_entry_is_a_404()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() => service.Delete("missing", null, default));

        Assert.Equal(ErrorCodes.TimeEntryNotFound, error.Code);
        Assert.Equal(404, error.Status);
    }

    // ---------- Closed periods ----------

    [Fact]
    public async Task Entries_in_a_closed_period_cannot_be_edited()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(date: "2026-02-20"), default);
        store.WithClosedPeriod(2026, 2);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Update(created.Id, Request(date: "2026-02-20", hours: 4m, version: created.Version), default));

        Assert.Equal(ErrorCodes.PeriodClosed, error.Code);
    }

    [Fact]
    public async Task Entries_in_a_closed_period_cannot_be_deleted()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(date: "2026-02-20"), default);
        store.WithClosedPeriod(2026, 2);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Delete(created.Id, created.Version, default));

        Assert.Equal(ErrorCodes.PeriodClosed, error.Code);
        Assert.Equal(1, store.EntryCount);
    }

    [Fact]
    public async Task Moving_an_entry_out_of_a_closed_month_is_rejected()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(date: "2026-02-20"), default);
        store.WithClosedPeriod(2026, 2);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Update(created.Id, Request(date: "2026-03-05", version: created.Version), default));

        Assert.Equal(ErrorCodes.PeriodClosed, error.Code);
    }

    [Fact]
    public async Task Moving_an_entry_into_a_closed_month_is_rejected()
    {
        var (service, store) = Build();
        var created = await service.Create(Request(date: "2026-03-05"), default);
        store.WithClosedPeriod(2026, 2);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.Update(created.Id, Request(date: "2026-02-20", version: created.Version), default));

        Assert.Equal(ErrorCodes.PeriodClosed, error.Code);
    }

    [Fact]
    public async Task Moving_between_two_open_months_recalculates_the_rate()
    {
        var (service, _) = Build();
        var created = await service.Create(Request(date: "2026-02-20"), default);

        var moved = await service.Update(created.Id, Request(date: "2026-03-05", version: created.Version), default);

        Assert.Equal(600m, moved.AppliedRate);
        Assert.Equal(4_800m, moved.Amount);
    }

    // ---------- Report ----------

    [Fact]
    public async Task Report_matches_the_acceptance_table()
    {
        var (service, _) = Build();

        await service.Create(Request(employeeId: "ivanov", projectId: "p001", date: "2026-02-20", hours: 8m), default);
        await service.Create(Request(employeeId: "ivanov", projectId: "p001", date: "2026-03-05", hours: 8m), default);
        await service.Create(Request(employeeId: "petrova", projectId: "p001", date: "2026-03-05", hours: 4m), default);
        await service.Create(Request(employeeId: "petrova", projectId: "p002", date: "2026-03-06", hours: 10m), default);

        var march = await service.Report(2026, 3, default);

        Assert.Equal(22m, march.TotalHours);
        Assert.Equal(14_600m, march.TotalAmount);

        var workshop = march.Items.Single(x => x.ProjectCode == "П-001");
        Assert.Equal(12m, workshop.Hours);
        Assert.Equal(7_600m, workshop.Amount);
        Assert.Equal(38m, workshop.Percent);
        Assert.False(workshop.IsOverspent);

        var networks = march.Items.Single(x => x.ProjectCode == "П-002");
        Assert.Equal(140m, networks.Percent);
        Assert.True(networks.IsOverspent);

        var february = await service.Report(2026, 2, default);
        Assert.Equal(8m, february.TotalHours);
        Assert.Equal(4_000m, february.TotalAmount);
        Assert.Equal(20m, february.Items.Single().Percent);
    }

    [Fact]
    public async Task Report_rejects_an_impossible_month()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() => service.Report(2026, 13, default));

        Assert.Equal(400, error.Status);
        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    // ---------- Retroactive rate changes ----------

    [Fact]
    public async Task Rate_change_recalculates_open_entries()
    {
        var (service, store) = Build();
        var march = await service.Create(Request(date: "2026-03-05"), default);

        var result = await service.UpdateRates(
            "ivanov",
            new RateUpdateRequest(
            [
                new RateInput(new DateOnly(2026, 1, 1), 500m),
                new RateInput(new DateOnly(2026, 3, 1), 650m),
            ]),
            default);

        Assert.Equal(1, result.Recalculated);
        Assert.Equal(0, result.SkippedInClosedPeriods);
        Assert.Equal(5_200m, store.Stored(march.Id).Amount);
    }

    [Fact]
    public async Task Rate_change_skips_closed_periods()
    {
        var (service, store) = Build();
        var february = await service.Create(Request(date: "2026-02-20"), default);
        var march = await service.Create(Request(date: "2026-03-05"), default);
        store.WithClosedPeriod(2026, 2);

        var result = await service.UpdateRates(
            "ivanov",
            new RateUpdateRequest(
            [
                new RateInput(new DateOnly(2026, 1, 1), 550m),
                new RateInput(new DateOnly(2026, 3, 1), 650m),
            ]),
            default);

        Assert.Equal(1, result.Recalculated);
        Assert.Equal(1, result.SkippedInClosedPeriods);
        Assert.Equal(4_000m, store.Stored(february.Id).Amount); // the closed month is untouched
        Assert.Equal(5_200m, store.Stored(march.Id).Amount);
    }

    [Fact]
    public async Task Rate_history_with_duplicate_dates_is_rejected()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() => service.UpdateRates(
            "ivanov",
            new RateUpdateRequest(
            [
                new RateInput(new DateOnly(2026, 1, 1), 500m),
                new RateInput(new DateOnly(2026, 1, 1), 700m),
            ]),
            default));

        Assert.Equal(ErrorCodes.RateHistoryInvalid, error.Code);
    }

    [Fact]
    public async Task Empty_rate_history_is_rejected()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.UpdateRates("ivanov", new RateUpdateRequest([]), default));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    // ---------- Listing ----------

    [Fact]
    public async Task List_totals_cover_the_whole_filter_not_just_the_page()
    {
        var (service, _) = Build();

        for (var day = 1; day <= 6; day++)
        {
            await service.Create(Request(date: $"2026-03-{day:00}", hours: 2m), default);
        }

        var page = await service.List(new TimeEntryQuery(2026, 3, null, null, Page: 1, PageSize: 2), default);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(12m, page.TotalHours); // the month total, not the two rows on the page
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNext);
        Assert.False(page.HasPrevious);
    }

    [Fact]
    public async Task List_rejects_an_impossible_page_size()
    {
        var (service, _) = Build();

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            service.List(new TimeEntryQuery(2026, 3, null, null, Page: 1, PageSize: 999), default));

        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task Empty_filters_do_not_hide_entries()
    {
        var (service, _) = Build();
        await service.Create(Request(), default);

        var page = await service.List(new TimeEntryQuery(2026, 3, string.Empty, string.Empty), default);

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Employee_filter_narrows_the_list()
    {
        var (service, _) = Build();
        await service.Create(Request(employeeId: "ivanov"), default);
        await service.Create(Request(employeeId: "petrova", hours: 4m), default);

        var page = await service.List(new TimeEntryQuery(2026, 3, "petrova", null), default);

        Assert.Single(page.Items);
        Assert.Equal("Петрова А. С.", page.Items[0].EmployeeName);
    }
}
