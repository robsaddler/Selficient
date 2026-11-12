namespace Selficient.Web.Pages;

/// <summary>
/// The facts the legal pages and the structured data share. Pinned by tests so they cannot drift
/// between pages, and kept in one place so a change of address is one edit.
/// </summary>
public static class LegalCopy
{
    /// <summary>Where to write.</summary>
    public const string ContactEmail = "hello@selficient.app";

    /// <summary>Who makes it, as the pages say it.</summary>
    public const string MadeBy = "Selficient is made in Blackwood, South Wales.";

    /// <summary>Who delivers the sign-in codes, as the privacy page names them.</summary>
    public const string MailProvider = "Postmark, a mail service";

    /// <summary>When the legal pages were last reviewed.</summary>
    public static DateOnly Reviewed { get; } = new(2026, 11, 12);

    /// <summary>
    /// The privacy page in four sentences, for the rail beside the prose. A legal page is a hard
    /// read by design; this is the version a person actually takes away, and on this site it is
    /// also the pitch, so it earns the space rather than filling it.
    /// </summary>
    public static IReadOnlyList<(string Heading, string Sentence)> PrivacyAtAGlance { get; } =
    [
        ("No account needed", "Everything works without one. Nobody is asked to sign up, and a plan made as a guest is a whole plan."),
        ("An account is yours to make and to delete", "If you make one, we keep your email address and your plan and nothing else. Deleting it removes both, straight away."),
        ("No tracking, no analytics", "Nobody is counting you, here or anywhere else. The only cookies are the ones that protect a form and keep you signed in."),
        ("Your plan stays on your device", "Your answers, your ticks and the pin on your map live in your own browser's storage. Clearing your browser clears them."),
        ("Your postcode goes once", "It goes to public lookups to find your area, your height above sea level and what the flood map says there, and it is not kept afterwards."),
        ("Photographs never touch our disk", "A photo is re-encoded in memory, read, and discarded in the same request."),
    ];

    /// <summary>The terms page in four sentences, for the rail beside the prose.</summary>
    public static IReadOnlyList<(string Heading, string Sentence)> TermsAtAGlance { get; } =
    [
        ("Guidance, not advice", "Careful guidance from public sources. It never replaces an electrician, a gas engineer or the emergency services."),
        ("Some links earn a commission", "At no cost to you, and said so beside the link. Products are chosen because they fit the action."),
        ("The engine decides, the model explains", "Days Ready and your plan come from rules you can read. A language model only counts and explains."),
        ("Free, as it is", "No charge and no promise that it is complete, correct or always up. The law of England and Wales governs these terms."),
    ];

    /// <summary>The upstream services the privacy page names, and what each receives.</summary>
    public static IReadOnlyList<(string Service, string Receives, string Purpose)> Upstreams { get; } =
    [
        ("postcodes.io", "your postcode", "to find your area, your nation and the coordinates of your postcode's centre"),
        ("postcodes.io", "your location, rounded to about ten metres, only when you press \"Use my location\"", "to find the postcode you are standing in and put it in the box for you; it is not kept"),
        ("Open-Meteo", "the coordinates of your postcode's centre", "to find your height above sea level"),
        ("The Environment Agency, Natural Resources Wales or SEPA, whichever keeps the flood map for your nation", "a box fifty metres across round your postcode's centre", "to read the flood map there, only when you leave the flood question for us to look up; Northern Ireland has no such service, so nothing is sent for a postcode there"),
        ("PVGIS, from the European Commission", "the coordinates of your postcode's centre, your roof's direction and pitch", "to find what a kilowatt of panels would make on your roof, only when you use the solar estimator"),
        ("Google Solar API", "the coordinates of your postcode's centre", "to describe the slopes of the nearest roof, only when you tick the consent box on the solar estimator and only if this site has a key for it"),
        ("Selficient, when you send a copy to another device", "an encrypted copy of your plan that we cannot read, because the key stays in the code on your screen", "to hand it to your other device, once; it is deleted as it is collected, or after ten minutes if it never is"),
        ("Postmark", "your email address and a sign-in code, only if you ask to sign in", "to deliver the code to you; nothing about your plan goes with it"),
        ("Anthropic", "a photograph you choose, re-encoded with its metadata removed, or the text of your plan", "to count what is in a cupboard, read a boiler plate or fuse board, or explain your plan, only when you ask; with your own key the photograph goes from your browser to Anthropic and never reaches us"),
    ];
}
