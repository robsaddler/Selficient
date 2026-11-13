using System.Globalization;

namespace Selficient.Domain.Parameters;

/// <summary>
/// Figures as a sentence says them: "two days", "a week", "1.2 kilowatt-hours", "600 watts". Every
/// sentence that states a figure is built with these from the figure itself, so a corrected figure
/// corrects the words that state it.
/// </summary>
public static class Say
{
    private static readonly string[] Words = ["none", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>A number: in words up to twelve when it is whole, with a comma in the thousands, and to two places otherwise.</summary>
    public static string Number(decimal value)
    {
        if (value == decimal.Truncate(value))
        {
            return value is >= 0m and <= 12m ? Words[(int)value] : value.ToString("N0", British);
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>A stretch of days: "half a day", "a day", "ten days", "a week", "three weeks", "a month".</summary>
    public static string Days(decimal days)
    {
        if (days == 0.5m)
        {
            return "half a day";
        }

        if (days == 30m)
        {
            return "a month";
        }

        if (days is >= 7m and <= 28m && days % 7m == 0m)
        {
            return One(days / 7m, "a week", "weeks");
        }

        return One(days, "a day", "days");
    }

    /// <summary>How long a stock lasts at what is used of it in a day, to a tenth of a day: "three days", "half a day".</summary>
    public static string Lasts(decimal quantity, decimal aDay) => Days(Math.Round(quantity / aDay, 1, MidpointRounding.AwayFromZero));

    /// <summary>A number of hours: "an hour", "sixteen hours" is "16 hours".</summary>
    public static string Hours(decimal hours) => One(hours, "an hour", "hours");

    /// <summary>An amount of water: "a litre", "nine litres", "380 litres".</summary>
    public static string Litres(decimal litres) => One(litres, "a litre", "litres");

    /// <summary>An amount of energy: watt-hours below a thousand, kilowatt-hours to one place from there.</summary>
    public static string Energy(decimal wattHours) => wattHours < 1000m
        ? $"{Number(wattHours)} watt-hours"
        : One(Math.Round(wattHours / 1000m, 1, MidpointRounding.AwayFromZero), "a kilowatt-hour", "kilowatt-hours");

    /// <summary>An amount of power: watts below a thousand, kilowatts to one place from there.</summary>
    public static string Power(decimal watts) => watts < 1000m
        ? $"{Number(watts)} watts"
        : One(Math.Round(watts / 1000m, 1, MidpointRounding.AwayFromZero), "a kilowatt", "kilowatts");

    /// <summary>A size in whole kilowatt-hours, as a name carries it: "1 kWh", "3 kWh".</summary>
    public static string KilowattHours(decimal wattHours) => Math.Round(wattHours / 1000m, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " kWh";

    /// <summary>The sentence with its first letter a capital.</summary>
    public static string Capital(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? text : char.ToUpper(text[0], British) + text[1..];
    }

    private static string One(decimal value, string one, string many) => value == 1m ? one : $"{Number(value)} {many}";
}
