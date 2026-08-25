using Timesheet.Domain.Errors;

namespace Timesheet.Domain.Policies;

/// <summary>
/// Месяц как полуинтервал <c>[Start; EndExclusive)</c>. Полуинтервал, а не <c>$year/$month</c>:
/// корректен на границах и, в отличие от вычисляемых полей, использует индекс по дате.
/// </summary>
public readonly record struct MonthRange(DateOnly Start, DateOnly EndExclusive)
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>
    /// Некорректный месяц — ошибка клиента (400), а не сбой сервера, поэтому
    /// <see cref="DomainException"/>, а не <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static MonthRange Create(int year, int month)
    {
        if (year is < MinYear or > MaxYear)
        {
            throw DomainException.Validation(
                $"Год должен быть в диапазоне {MinYear}–{MaxYear}, получено {year}.",
                "year");
        }

        if (month is < 1 or > 12)
        {
            throw DomainException.Validation(
                $"Месяц должен быть в диапазоне 1–12, получено {month}.",
                "month");
        }

        var start = new DateOnly(year, month, 1);
        return new MonthRange(start, start.AddMonths(1));
    }

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;
}
