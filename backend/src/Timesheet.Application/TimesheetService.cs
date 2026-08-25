using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
using Timesheet.Domain.Policies;
namespace Timesheet.Application;
public sealed class TimesheetService(ITimesheetStore store)
{
    public Task<PagedResult<TimeEntryView>> List(TimeEntryQuery query, CancellationToken token)
    {
        _ = MonthRange.Create(query.Year, query.Month);
        if (query.Page < 1 || query.PageSize is < 1 or > 100) throw new DomainException("VALIDATION_FAILED", "Страница должна быть положительной, размер страницы — от 1 до 100.");
        return store.List(query, token);
    }
    public Task<ProjectReport> Report(int year, int month, CancellationToken token) { _ = MonthRange.Create(year, month); return store.Report(year, month, token); }
    public Task<IReadOnlyList<LookupItem>> Employees(CancellationToken token) => store.Employees(token);
    public Task<IReadOnlyList<LookupItem>> Projects(CancellationToken token) => store.Projects(token);

    public async Task<TimeEntry> Create(SaveTimeEntryRequest request, CancellationToken token)
    {
        var context = await Context(request, null, token);
        var rate = TimeEntryPolicy.ValidateAndCalculate(false, context.Closed, request.Hours, context.Project, context.Employee, request.Date, context.DailyHours);
        var now = DateTime.UtcNow;
        return await store.Insert(new TimeEntry { Id = Guid.NewGuid().ToString("N"), EmployeeId = request.EmployeeId, ProjectId = request.ProjectId, Date = request.Date, Hours = request.Hours, Comment = request.Comment.Trim(), AppliedRate = rate, Amount = Money.Calculate(request.Hours, rate), Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now }, token);
    }
    public async Task<TimeEntry> Update(string id, SaveTimeEntryRequest request, CancellationToken token)
    {
        if (request.Version is null) throw new DomainException("VALIDATION_FAILED", "Для изменения обязательна версия записи.");
        var current = await store.GetEntry(id, token) ?? throw NotFound("TIME_ENTRY_NOT_FOUND", "Запись табеля не найдена.");
        var context = await Context(request, id, token);
        var oldClosed = await store.IsPeriodClosed(current.Date, token);
        var rate = TimeEntryPolicy.ValidateAndCalculate(oldClosed, context.Closed, request.Hours, context.Project, context.Employee, request.Date, context.DailyHours);
        current.EmployeeId = request.EmployeeId; current.ProjectId = request.ProjectId; current.Date = request.Date; current.Hours = request.Hours; current.Comment = request.Comment.Trim(); current.AppliedRate = rate; current.Amount = Money.Calculate(request.Hours, rate); current.Version++; current.UpdatedAtUtc = DateTime.UtcNow;
        return await store.Replace(current, request.Version.Value, token) ?? throw new DomainException("CONCURRENCY_CONFLICT", "Запись уже изменена другим пользователем. Перечитайте данные и повторите попытку.", 409);
    }
    public async Task Delete(string id, long? version, CancellationToken token)
    {
        var entry = await store.GetEntry(id, token) ?? throw NotFound("TIME_ENTRY_NOT_FOUND", "Запись табеля не найдена.");
        if (await store.IsPeriodClosed(entry.Date, token)) throw new DomainException("PERIOD_CLOSED", "Закрытый период нельзя изменять.", 409);
        if (!await store.Delete(id, version, token)) throw new DomainException("CONCURRENCY_CONFLICT", "Запись уже изменена другим пользователем.", 409);
    }
    public Task Close(int year, int month, bool closed, CancellationToken token) { _ = MonthRange.Create(year, month); return store.SetPeriod(year, month, closed, token); }
    public Task Seed(CancellationToken token) => store.Seed(token);
    public Task<RecalculationResult> UpdateRates(string id, RateUpdateRequest request, CancellationToken token)
    {
        if (request.Rates.Count == 0 || request.Rates.Any(x => x.Value <= 0)) throw new DomainException("VALIDATION_FAILED", "История ставок не может быть пустой, ставки должны быть положительными.");
        return store.UpdateRates(id, request.Rates.Select(x => new HourlyRate(x.ValidFrom, x.Value)).ToArray(), token);
    }
    private async Task<(Employee Employee, Project Project, bool Closed, decimal DailyHours)> Context(SaveTimeEntryRequest request, string? excludingId, CancellationToken token)
    {
        var employee = await store.GetEmployee(request.EmployeeId, token) ?? throw NotFound("EMPLOYEE_NOT_FOUND", "Сотрудник не найден.");
        var project = await store.GetProject(request.ProjectId, token) ?? throw NotFound("PROJECT_NOT_FOUND", "Проект не найден.");
        var closed = await store.IsPeriodClosed(request.Date, token);
        var dailyHours = await store.GetDailyHours(request.EmployeeId, request.Date, excludingId, token);
        return (employee, project, closed, dailyHours);
    }
    private static DomainException NotFound(string code, string message) => new(code, message, 404);
}
