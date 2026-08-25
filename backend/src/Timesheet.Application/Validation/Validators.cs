using FluentValidation;
using FluentValidation.Results;
using Timesheet.Application.Contracts;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Policies;

namespace Timesheet.Application.Validation;

// Валидация входа отвечает на вопрос «запрос синтаксически осмыслен?»: обязательные поля,
// диапазоны, формат. Бизнес-правила (ставка на дату, закрытый период, суточный лимит) живут
// в Timesheet.Domain — им нужны данные из хранилища.
//
// Кратность часов проверяется в обоих слоях намеренно: валидатор даёт пользователю ошибку
// с именем поля, домен держит инвариант для записей, пришедших мимо HTTP (сидер, пересчёт).
public static class ValidationErrors
{
    /// <summary>Результат FluentValidation → доменная 400 с разбивкой по полям.</summary>
    public static DomainException ToDomainException(this ValidationResult result)
    {
        var fields = result.Errors
            .GroupBy(x => Camelize(x.PropertyName))
            .ToDictionary(
                g => g.Key,
                g => (object?)g.Select(x => x.ErrorMessage).ToArray());

        var message = string.Join(" ", result.Errors.Select(x => x.ErrorMessage).Distinct());

        return new DomainException(
            ErrorCodes.ValidationFailed,
            message.Length == 0 ? "Запрос содержит некорректные данные." : message,
            400,
            new Dictionary<string, object?> { ["fields"] = fields });
    }

    private static string Camelize(string property)
    {
        if (property.Length == 0)
        {
            return property;
        }

        return char.ToLowerInvariant(property[0]) + property[1..];
    }
}

public sealed class SaveTimeEntryRequestValidator : AbstractValidator<SaveTimeEntryRequest>
{
    public SaveTimeEntryRequestValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Выберите сотрудника.");

        RuleFor(x => x.ProjectId)
            .NotEmpty().WithMessage("Выберите проект.");

        RuleFor(x => x.Date)
            .Must(BeAPlausibleBusinessDate)
            .WithMessage($"Дата должна быть в диапазоне {MonthRange.MinYear}–{MonthRange.MaxYear} годов.");

        RuleFor(x => x.Hours)
            .GreaterThan(0m).WithMessage("Часы должны быть больше нуля.")
            .LessThanOrEqualTo(WorkHoursPolicy.DailyLimit)
            .WithMessage($"За одну запись нельзя указать больше {WorkHoursPolicy.DailyLimit:0} часов.")
            .Must(x => decimal.Remainder(x, WorkHoursPolicy.Step) == 0)
            .WithMessage("Часы задаются с шагом 0,5.");

        RuleFor(x => x.Comment)
            .NotNull().WithMessage("Комментарий не может быть null.")
            .MaximumLength(300).WithMessage("Комментарий не длиннее 300 символов.");

        RuleFor(x => x.Version)
            .GreaterThan(0L)
            .When(x => x.Version.HasValue)
            .WithMessage("Версия записи должна быть положительной.");
    }

    private static bool BeAPlausibleBusinessDate(DateOnly date) =>
        date.Year >= MonthRange.MinYear && date.Year <= MonthRange.MaxYear;
}

public sealed class TimeEntryQueryValidator : AbstractValidator<TimeEntryQuery>
{
    public TimeEntryQueryValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(MonthRange.MinYear, MonthRange.MaxYear)
            .WithMessage($"Год должен быть в диапазоне {MonthRange.MinYear}–{MonthRange.MaxYear}.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12)
            .WithMessage("Месяц должен быть в диапазоне 1–12.");

        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Номер страницы начинается с единицы.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, Paging.MaxPageSize)
            .WithMessage($"Размер страницы — от 1 до {Paging.MaxPageSize}.");
    }
}

public sealed class PeriodRequestValidator : AbstractValidator<PeriodRequest>
{
    public PeriodRequestValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(MonthRange.MinYear, MonthRange.MaxYear)
            .WithMessage($"Год должен быть в диапазоне {MonthRange.MinYear}–{MonthRange.MaxYear}.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12)
            .WithMessage("Месяц должен быть в диапазоне 1–12.");
    }
}

public sealed class RateUpdateRequestValidator : AbstractValidator<RateUpdateRequest>
{
    public RateUpdateRequestValidator()
    {
        RuleFor(x => x.Rates)
            .NotEmpty().WithMessage("История ставок не может быть пустой.");

        RuleForEach(x => x.Rates).ChildRules(rate =>
        {
            rate.RuleFor(x => x.Value)
                .GreaterThan(0m).WithMessage("Ставка должна быть положительной.");

            rate.RuleFor(x => x.ValidFrom)
                .Must(x => x.Year >= MonthRange.MinYear && x.Year <= MonthRange.MaxYear)
                .WithMessage($"Дата начала действия ставки — в диапазоне {MonthRange.MinYear}–{MonthRange.MaxYear} годов.");
        });
    }
}
