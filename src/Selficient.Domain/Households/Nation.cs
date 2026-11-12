namespace Selficient.Domain.Households;

/// <summary>
/// The nation a home is in, which decides whose flood map, whose warnings and whose grants apply.
/// It lives with the household because the rules that build a plan need it: an action that says
/// "check your flood risk" has to send a house in Perth to Scotland's service, not England's.
/// </summary>
public enum Nation
{
    /// <summary>Not determined.</summary>
    Unknown,

    /// <summary>England.</summary>
    England,

    /// <summary>Wales.</summary>
    Wales,

    /// <summary>Scotland.</summary>
    Scotland,

    /// <summary>Northern Ireland.</summary>
    NorthernIreland,
}

/// <summary>
/// The water a flood band is about. The warning services cover rivers and the sea; nobody warns a
/// street that its drains are about to be overwhelmed, so a house at risk from surface water alone
/// is not told to sign up for a warning it will never be sent.
/// </summary>
public enum FloodWater
{
    /// <summary>Not said: the household gave the band itself, and is treated as rivers or the sea.</summary>
    NotSaid,

    /// <summary>Rivers or the sea.</summary>
    RiversOrSea,

    /// <summary>Surface water and small watercourses: rain that the drains and the ground cannot take.</summary>
    SurfaceWater,
}
