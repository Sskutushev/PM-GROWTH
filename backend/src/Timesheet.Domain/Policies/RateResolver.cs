using Timesheet.Domain.Errors;
using Timesheet.Domain.Models;
namespace Timesheet.Domain.Policies;
public static class RateResolver
{
    public static decimal Resolve(IReadOnlyCollection<HourlyRate> rates, DateOnly date)
    {
        var matches = rates.Where(x => x.ValidFrom <= date).OrderByDescending(x => x.ValidFrom).ToArray();
        if (matches.Length == 0) throw new DomainException("RATE_NOT_FOUND", $"На {date:dd.MM.yyyy} для сотрудника не задана ставка.");
        if (matches.Length > 1 && matches[0].ValidFrom == matches[1].ValidFrom) throw new DomainException("RATE_HISTORY_INVALID", $"На {matches[0].ValidFrom:dd.MM.yyyy} задано несколько ставок.");
        return matches[0].Value;
    }
}
