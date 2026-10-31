using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Security;

namespace Selficient.TestKit;

/// <summary>
/// Signing a test client in to the reviewer the way a person does: an account in the real store,
/// the real password step, then a real time-based code made from the account's own seed. No test
/// takes a short cut past the second factor, because the second factor is the point of it.
/// </summary>
public static partial class OperatorSignInHelper
{
    /// <summary>The username every test signs in as unless it says otherwise.</summary>
    public const string Username = "rob";

    /// <summary>The password every test signs in with. Long enough for the policy, and not a secret.</summary>
    public const string Password = "the lantern on the kitchen table";

    /// <summary>Makes an operator with its second factor already set up, and answers its recovery codes.</summary>
    public static async Task<IReadOnlyList<string>> CreateOperatorAsync(
        this SelficientWebApplicationFactory factory,
        string username = Username,
        string password = Password,
        bool mustChangePassword = false,
        bool enrolled = true)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using IServiceScope scope = factory.Services.CreateScope();
        IOperatorStore store = scope.ServiceProvider.GetRequiredService<IOperatorStore>();
        OperatorSignIn signIn = scope.ServiceProvider.GetRequiredService<OperatorSignIn>();
        TimeProvider clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        // The test database belongs to the host, not to one test class, so a name may already be
        // there from another suite. Reusing it and writing this test's password and seed over the
        // top keeps every test independent without a database each.
        OperatorAccount? existing = await store.FindAsync(username, CancellationToken.None);
        long id;
        if (existing is null)
        {
            id = await store.CreateAsync(username, username, PasswordHasher.Hash(password), mustChangePassword, clock.GetUtcNow(), CancellationToken.None);
        }
        else
        {
            id = existing.Id;
            await store.SetPasswordAsync(id, PasswordHasher.Hash(password), Guid.NewGuid(), clock.GetUtcNow(), CancellationToken.None);
            await store.SetEnabledAsync(id, true, Guid.NewGuid(), CancellationToken.None);
        }

        if (!enrolled)
        {
            return [];
        }

        IReadOnlyList<string> codes = RecoveryCodes.Issue();
        await store.SetSecondFactorAsync(id, signIn.ProtectSecret(Totp.NewSecret()), clock.GetUtcNow(), signIn.ProtectCodes(codes), Guid.NewGuid(), CancellationToken.None);
        return codes;
    }

    /// <summary>The code an authenticator would be showing for this operator at this moment.</summary>
    public static async Task<string> CodeForAsync(this SelficientWebApplicationFactory factory, string username = Username)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using IServiceScope scope = factory.Services.CreateScope();
        IOperatorStore store = scope.ServiceProvider.GetRequiredService<IOperatorStore>();
        OperatorSignIn signIn = scope.ServiceProvider.GetRequiredService<OperatorSignIn>();
        TimeProvider clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        OperatorAccount account = (await store.FindAsync(username, CancellationToken.None))!;
        return Totp.CodeAt(signIn.ReadSecret(account.TotpSecretProtected)!, clock.GetUtcNow());
    }

    /// <summary>
    /// Takes both steps on a client, answering the page the second step lands on. The caller is
    /// told nothing it could not have read off the screen.
    /// </summary>
    public static async Task<string> SignInAsync(
        this SelficientWebApplicationFactory factory,
        HttpClient client,
        string username = Username,
        string password = Password,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(client);
        string token = await TokenAsync(client, "/admin/sign-in");
        using HttpResponseMessage passwordStep = await client.PostAsync(
            new Uri("/admin/sign-in", UriKind.Relative),
            Form(token, ("Username", username), ("Password", password)));
        // A client that follows redirects has already landed on the next step and answers 200; one
        // that does not answers 302 and names it in the header. Both are allowed here, so a test
        // can choose whichever makes its own assertions readable.
        string? next = Next(passwordStep, "/admin/sign-in");
        if (next is null)
        {
            return await passwordStep.Content.ReadAsStringAsync();
        }
        string codeToken = await TokenAsync(client, next);
        using HttpResponseMessage codeStep = await client.PostAsync(
            new Uri(next, UriKind.Relative),
            Form(codeToken, ("Code", code ?? await factory.CodeForAsync(username))));
        string? landing = Next(codeStep, next);
        if (landing is null)
        {
            return await codeStep.Content.ReadAsStringAsync();
        }

        using HttpResponseMessage landed = await client.GetAsync(new Uri(landing, UriKind.Relative));
        return await landed.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Where a step went: the path the redirect named, or the path the client was left on when it
    /// followed the redirect itself. Nothing when the step stayed on the page it was posted to,
    /// which is what a refusal looks like.
    /// </summary>
    private static string? Next(HttpResponseMessage response, string postedTo)
    {
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Uri location = response.Headers.Location!;
            return location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();
        }

        Uri? landed = response.RequestMessage?.RequestUri;
        return landed is not null && !landed.AbsolutePath.Equals(postedTo, StringComparison.OrdinalIgnoreCase)
            ? landed.PathAndQuery
            : null;
    }

    /// <summary>The antiforgery token on a page, which every post the reviewer takes must carry.</summary>
    public static async Task<string> TokenAsync(HttpClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);
        string html = await client.GetStringAsync(new Uri(path, UriKind.Relative));
        return TokenPattern().Match(html).Groups["token"].Value;
    }

    /// <summary>A form body with its antiforgery token.</summary>
    public static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new FormUrlEncodedContent([
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            .. fields.Select(static f => new KeyValuePair<string, string>(f.Name, f.Value)),
        ]);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenPattern();
}
