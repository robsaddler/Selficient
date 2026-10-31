namespace Selficient.Application.Share;

/// <summary>
/// The one thing a household pastes into a group chat: how many days their house stands in a
/// scenario. It carries a number and a scenario the engine already names, and nothing about the
/// house, so the card can be drawn for anyone and cached for everyone.
/// </summary>
/// <param name="Days">Days Ready, in half days.</param>
/// <param name="ScenarioId">The scenario's id, for the file name.</param>
/// <param name="ScenarioName">The scenario as the engine names it ("A storm takes the power out").</param>
public sealed record ShareCard(decimal Days, string ScenarioId, string ScenarioName)
{
    /// <summary>The card's size, the one every chat app expects.</summary>
    public const int Width = 1200;

    /// <summary>The card's height.</summary>
    public const int Height = 630;

    /// <summary>The figure as the card writes it: "1", "1.5", "7".</summary>
    public string Figure => Days.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"day" or "days".</summary>
    public string Unit => Days == 1m ? "day" : "days";

    /// <summary>The scenario as a clause: "when a storm takes the power out".</summary>
    public string Clause => "when " + char.ToLowerInvariant(ScenarioName[0]) + ScenarioName[1..];
}

/// <summary>Draws a share card as a PNG.</summary>
public interface IShareCardRenderer
{
    /// <summary>The card, as PNG bytes.</summary>
    byte[] Render(ShareCard card);
}
