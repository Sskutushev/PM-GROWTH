using System.Globalization;
using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;

namespace Timesheet.Domain.Policies;

/// <summary>
/// Ставка, действовавшая на дату: максимальный <c>ValidFrom &lt;= date</c>.
/// Наивное «взять первую из списка» даёт неверные деньги.
/// </summary>
public static class RateResolver
{
    public static decimal Resolve(IReadOnlyCollection<HourlyRate> rates, DateOnly date)
    {
        if (TryResolve(rates, date, out var rate))
        {
            return rate;
        }

        throw new DomainException(
            ErrorCodes.RateNotFound,
            $"На {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} для сотрудника не задана ставка.",
            400,
            new Dictionary<string, object?> { ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    /// <summary>Без исключения — для проверки плана пересчёта, где отсутствие ставки ожидаемо.</summary>
    public static bool TryResolve(IReadOnlyCollection<HourlyRate> rates, DateOnly date, out decimal rate)
    {
        rate = 0m;

        var applicable = rates
            .Where(x => x.ValidFrom <= date)
            .OrderByDescending(x => x.ValidFrom)
            .ToArray();

        if (applicable.Length == 0)
        {
            return false;
        }

        // Две ставки с одной датой начала делают расчёт недетерминированным:
        // это порча справочника, а не «выберем любую».
        if (applicable.Length > 1 && applicable[0].ValidFrom == applicable[1].ValidFrom)
        {
            throw new DomainException(
                ErrorCodes.RateHistoryInvalid,
                $"На {applicable[0].ValidFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} задано несколько ставок.");
        }

        rate = applicable[0].Value;
        return true;
    }

    /// <summary>Проверяет непротиворечивость всей истории ставок до того, как она будет сохранена.</summary>
    public static void EnsureHistoryIsValid(IReadOnlyCollection<HourlyRate> rates)
    {
        if (rates.Count == 0)
        {
            throw DomainException.Validation("История ставок не может быть пустой.", "rates");
        }

        if (rates.Any(x => x.Value <= 0))
        {
            throw DomainException.Validation("Ставка должна быть положительной.", "rates");
        }

        var duplicate = rates
            .GroupBy(x => x.ValidFrom)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new DomainException(
                ErrorCodes.RateHistoryInvalid,
                $"На {duplicate.Key.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} задано несколько ставок.");
        }
    }
}
