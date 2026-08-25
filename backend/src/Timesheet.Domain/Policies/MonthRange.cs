using Timesheet.Domain.Errors;

namespace Timesheet.Domain.Policies;

/// <summary>
/// A month as the half-open interval <c>[Start; EndExclusive)</c>. Half-open rather than
/// <c>$year/$month</c>: correct at the boundaries and, unlike computed fields, index-friendly.
/// </summary>
public readonly record struct MonthRange(DateOnly Start, DateOnly EndExclusive)
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>
    /// An impossible month is a client error (400), not a server fault, hence
    /// <see cref="DomainException"/> rather than <see cref="ArgumentOutOfRangeException"/>.
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
