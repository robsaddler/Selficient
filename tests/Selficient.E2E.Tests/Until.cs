using Microsoft.Playwright;

namespace Selficient.E2E.Tests;

/// <summary>
/// Waiting for something that takes a promise to find out: what is in the browser's store, what
/// the account holds. <c>WaitForFunctionAsync</c> does not do this. It polls a function and takes
/// whatever comes back as the answer, and a promise is truthy the moment it is made, so an
/// <c>async</c> predicate "passes" at once and waits for nothing. Four sync journeys failed on
/// state that had not arrived yet before that was found. <c>EvaluateAsync</c> does wait for a
/// promise, so this asks with that, again and again, until the answer is yes.
/// </summary>
internal static class Until
{
    /// <summary>Waits until an expression, which may be async, answers true in the page.</summary>
    public static async Task TrueAsync(IPage page, string expression, object? arg = null, int seconds = 15)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try
            {
                if (await page.EvaluateAsync<bool>(expression, arg))
                {
                    return;
                }
            }
            catch (PlaywrightException) when (DateTime.UtcNow < deadline)
            {
                // The page was between two documents; ask the next one.
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"After {seconds} seconds this was still not true in the page: {expression}");
            }

            await Task.Delay(100);
        }
    }
}
