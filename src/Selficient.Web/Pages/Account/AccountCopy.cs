namespace Selficient.Web.Pages.Account;

/// <summary>
/// What the account pages say. An account is an email address and a copy of the plan, and it does
/// two things; these sentences say those two things and nothing else, and never suggest that one
/// is needed.
/// </summary>
public static class AccountCopy
{
    /// <summary>The sign-in page's heading. The same page makes an account and signs in to one.</summary>
    public static string SignInHeading => "Sign in or create an account";

    /// <summary>What an account is, in the page's opening line.</summary>
    public static string SignInLede => "Just an email address, no password. We send a six-digit code to it; type the code in and you are signed in. It works the same the first time and every time after.";

    /// <summary>The two things an account does.</summary>
    public static IReadOnlyList<string> WhatItDoes { get; } =
    [
        "It keeps every device you sign in on in step.",
        "It keeps your plan if this device is lost.",
    ];

    /// <summary>Under the form: the thing a visitor must never be left unsure of.</summary>
    public static string NotNeeded => "You do not need an account to use Selficient. Your plan is saved on this device either way.";

    /// <summary>The button that sends the code.</summary>
    public static string SendCode => "Send me a code";

    /// <summary>The code page's heading.</summary>
    public static string CodeHeading => "Check your email";

    /// <summary>What was sent and where, before the address.</summary>
    public static string CodeSentTo => "We have sent a six-digit code to";

    /// <summary>What the code is good for, after the address.</summary>
    public static string CodeGoodFor => "It works for ten minutes, once, and only in this browser.";

    /// <summary>The button that proves the code.</summary>
    public static string SignIn => "Sign in";

    /// <summary>The button that sends another.</summary>
    public static string Resend => "Send a new code";

    /// <summary>Said when another has been sent.</summary>
    public static string Resent => "A new code is on its way. The one before it no longer works in this browser.";

    /// <summary>The link back to the address.</summary>
    public static string OtherAddress => "Use a different address";

    /// <summary>The sign-out page's heading.</summary>
    public static string SignOutHeading => "Sign out of this device";

    /// <summary>What signing out does and does not do.</summary>
    public static string SignOutLede => "Signing out stops this device following your account. Your account, and the plan kept in it, stay as they are.";

    /// <summary>The one thing signing out asks.</summary>
    public static string KeepACopy => "Keep a copy of my plan on this device";

    /// <summary>What unticking it does.</summary>
    public static string KeepACopyHint => "Untick this on a shared or borrowed device and the plan is removed from this browser as you sign out.";

    /// <summary>The sign-out button.</summary>
    public static string SignOut => "Sign out";

    /// <summary>The delete page's heading.</summary>
    public static string DeleteHeading => "Delete your account";

    /// <summary>What deleting removes, and when.</summary>
    public static string DeleteLede => "This removes your email address, the copy of your plan we keep, your passkeys and every sign-in, straight away. It cannot be undone.";

    /// <summary>What deleting leaves.</summary>
    public static string DeleteLeaves => "The plan on this device stays, and Selficient goes on working here without an account.";

    /// <summary>The delete button.</summary>
    public static string Delete => "Delete my account";

    /// <summary>The way out of deleting.</summary>
    public static string KeepAccount => "Keep my account";

    /// <summary>The heading once it is gone.</summary>
    public static string DeletedHeading => "Your account has been deleted";

    /// <summary>What is true once it is gone.</summary>
    public static string DeletedLede => "We no longer hold your email address or a copy of your plan. The plan on this device is still here, and you do not need an account to go on using it.";
}
