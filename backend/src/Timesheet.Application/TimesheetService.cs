using FluentValidation;
using Timesheet.Application.Contracts;
using Timesheet.Application.Validation;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;

namespace Timesheet.Application;

/// <summary>
/// Orchestration: validate input, load context, apply domain rules, persist.
/// Holds no business rules of its own and knows nothing about HTTP or Mongo.
/// </summary>
public sealed class TimesheetService(
    ITimesheetStore store,
    IValidator<SaveTimeEntryRequest> saveValidator,
    IValidator<TimeEntryQuery> queryValidator,
    IValidator<RateUpdateRequest> rateValidator)
{
    // ---------- Reads ----------

    public Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken token)
    {
        Validate(queryValidator, query);
        return store.List(query, token);
    }

    public Task<ProjectReport> Report(int year, int month, CancellationToken token)
    {
        _ = MonthRange.Create(year, month); // throws a domain 400 for a month out of range
        return store.Report(year, month, token);
    }

    public Task<IReadOnlyList<LookupItem>> Employees(CancellationToken token) => store.Employees(token);

    public Task<IReadOnlyList<LookupItem>> Projects(CancellationToken token) => store.Projects(token);

    // ---------- Time entry writes ----------

    public async Task<TimeEntry> Create(SaveTimeEntryRequest request, CancellationToken token)
    {
        Validate(saveValidator, request);

        var context = await LoadContext(request, excludingId: null, sourceDate: null, token);
        var rate = TimeEntryPolicy.ValidateAndResolveRate(context);

        var now = DateTime.UtcNow;
        var entry = new TimeEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            EmployeeId = request.EmployeeId,
            ProjectId = request.ProjectId,
            Date = request.Date,
            Hours = request.Hours,
            Comment = request.Comment.Trim(),
            AppliedRate = rate,
            Amount = Money.Calculate(request.Hours, rate),
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        return await store.Insert(entry, token);
    }

    public async Task<TimeEntry> Update(string id, SaveTimeEntryRequest request, CancellationToken token)
    {
        Validate(saveValidator, request);

        if (request.Version is null)
        {
            throw DomainException.Validation("Для изменения обязательна версия записи.", "version");
        }

        var current = await store.GetEntry(id, token)
            ?? throw DomainException.NotFound(ErrorCodes.TimeEntryNotFound, "Запись табеля не найдена.");

        // Both months are checked: the one the entry leaves and the one it moves into.
        var context = await LoadContext(request, excludingId: id, sourceDate: current.Date, token);
        var rate = TimeEntryPolicy.ValidateAndResolveRate(context);

        current.EmployeeId = request.EmployeeId;
        current.ProjectId = request.ProjectId;
        current.Date = request.Date;
        current.Hours = request.Hours;
        current.Comment = request.Comment.Trim();
        current.AppliedRate = rate;
        current.Amount = Money.Calculate(request.Hours, rate);
        current.Version++;
        current.UpdatedAtUtc = DateTime.UtcNow;

        return await store.Replace(current, request.Version.Value, token)
            ?? throw DomainException.Conflict(
                ErrorCodes.ConcurrencyConflict,
                "Запись уже изменена другим пользователем. Перечитайте данные и повторите попытку.");
    }

    public async Task Delete(string id, long? version, CancellationToken token)
    {
        var entry = await store.GetEntry(id, token)
            ?? throw DomainException.NotFound(ErrorCodes.TimeEntryNotFound, "Запись табеля не найдена.");

        var closed = await store.IsPeriodClosed(entry.Date, token);
        TimeEntryPolicy.EnsurePeriodsAreOpen(closed, closed);

        if (!await store.Delete(id, version, token))
        {
            throw DomainException.Conflict(
                ErrorCodes.ConcurrencyConflict,
                "Запись уже изменена другим пользователем. Перечитайте данные и повторите попытку.");
        }
    }

    // ---------- Periods and rates ----------

    public Task SetPeriod(int year, int month, bool closed, CancellationToken token)
    {
        _ = MonthRange.Create(year, month);
        return store.SetPeriod(year, month, closed, token);
    }

    public Task<RecalculationResult> UpdateRates(string id, RateUpdateRequest request, CancellationToken token)
    {
        Validate(rateValidator, request);

        var rates = request.Rates
            .Select(x => new HourlyRate(x.ValidFrom, x.Value))
            .ToArray();

        RateResolver.EnsureHistoryIsValid(rates);

        return store.UpdateRates(id, rates, token);
    }

    public Task Seed(CancellationToken token) => store.Seed(token);

    // ---------- Internals ----------

    private async Task<TimeEntryContext> LoadContext(
        SaveTimeEntryRequest request,
        string? excludingId,
        DateOnly? sourceDate,
        CancellationToken token)
    {
        var employee = await store.GetEmployee(request.EmployeeId, token)
            ?? throw DomainException.NotFound(ErrorCodes.EmployeeNotFound, "Сотрудник не найден.");

        var project = await store.GetProject(request.ProjectId, token)
            ?? throw DomainException.NotFound(ErrorCodes.ProjectNotFound, "Проект не найден.");

        var targetClosed = await store.IsPeriodClosed(request.Date, token);

        // There is no source period when creating. When moving an entry to another month,
        // neither the source nor the target month may be closed.
        var sourceClosed = sourceDate switch
        {
            null => false,
            { } date when date == request.Date => targetClosed,
            { } date => await store.IsPeriodClosed(date, token),
        };

        var dailyHours = await store.GetDailyHours(request.EmployeeId, request.Date, excludingId, token);

        return new TimeEntryContext(
            Employee: employee,
            Project: project,
            Date: request.Date,
            Hours: request.Hours,
            SourcePeriodClosed: sourceClosed,
            TargetPeriodClosed: targetClosed,
            HoursAlreadyLoggedThatDay: dailyHours);
    }

    private static void Validate<T>(IValidator<T> validator, T instance)
    {
        var result = validator.Validate(instance);
        if (!result.IsValid)
        {
            throw result.ToDomainException();
        }
    }
}
