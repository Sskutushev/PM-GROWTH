namespace Timesheet.Domain.Policies;

public static class Money
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal Calculate(decimal hours, decimal rate) => Round(hours * rate);
}
