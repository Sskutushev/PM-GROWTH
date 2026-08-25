namespace Timesheet.Domain.Policies;

/// <summary>
/// Единственное место округления денег. Половина округляется от нуля, а не «к чётному»,
/// как делает <c>Math.Round(value, 2)</c> по умолчанию.
/// </summary>
public static class Money
{
    public const int Scale = 2;

    public static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Округляем каждую запись, затем суммируем округлённые: иначе итог отчёта
    /// не сойдётся с суммой видимых строк.
    /// </summary>
    public static decimal Calculate(decimal hours, decimal rate) => Round(hours * rate);
}
