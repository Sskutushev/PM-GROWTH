namespace Timesheet.Domain.Policies;
public readonly record struct MonthRange(DateOnly Start, DateOnly EndExclusive)
{
    public static MonthRange Create(int year, int month)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var start = new DateOnly(year, month, 1);
        return new(start, start.AddMonths(1));
    }
    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;
}
