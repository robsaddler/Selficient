using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Options;
using Selficient.Application.Accounts;

namespace Selficient.Infrastructure.Accounts;

/// <summary>
/// Who the passkeys are for: the name a browser shows beside the fingerprint prompt, the domain a
/// credential is bound to, and the origins a ceremony may come from. A credential made for one
/// domain cannot be used on another, which is the whole of a passkey's resistance to phishing, so
/// these are the site's own address and nothing looser.
/// </summary>
public sealed class PasskeyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Passkeys";

    /// <summary>The domain credentials are bound to. <c>localhost</c> on a development machine.</summary>
    [Required]
    public string RelyingPartyId { get; init; } = "selficient.app";

    /// <summary>The name shown in the browser's prompt.</summary>
    [Required]
    [MaxLength(64)]
    public string RelyingPartyName { get; init; } = "Selficient";

    /// <summary>The origins a ceremony may come from, scheme and port included.</summary>
    [MinLength(1)]
    public IReadOnlyList<string> Origins { get; init; } = ["https://selficient.app"];
}

/// <summary>
/// The WebAuthn ceremonies, by Fido2NetLib. Credentials are discoverable and user verification is
/// required, so a sign-in needs no address typed and a stolen, unlocked key is not enough.
/// Attestation is not asked for: we do not need to know the make of an authenticator. Every way a
/// response can fail to hold is answered as nothing, and the caller says one sentence.
/// </summary>
public sealed class Fido2PasskeyCeremony : IPasskeyCeremony
{
    private readonly Fido2 _fido2;

    /// <summary>Creates the ceremonies for the site the options name.</summary>
    public Fido2PasskeyCeremony(IOptions<PasskeyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PasskeyOptions passkeys = options.Value;
        _fido2 = new Fido2(new Fido2Configuration
        {
            ServerDomain = passkeys.RelyingPartyId,
            ServerName = passkeys.RelyingPartyName,
            Origins = new HashSet<string>(passkeys.Origins, StringComparer.Ordinal),
            ChallengeSize = 32,
        });
    }

    /// <summary>The account's number as the user handle an authenticator stores with the credential: eight bytes, and nothing that names a person.</summary>
    public static byte[] UserHandle(long accountId)
    {
        byte[] handle = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(handle, accountId);
        return handle;
    }

    /// <inheritdoc />
    public string RegistrationOptions(long accountId, string email, IReadOnlyList<byte[]> existing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(existing);
        return _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = UserHandle(accountId), Name = email, DisplayName = email },
            ExcludeCredentials = [.. existing.Select(static id => new PublicKeyCredentialDescriptor(id))],
            AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Required },
            AttestationPreference = AttestationConveyancePreference.None,
        }).ToJson();
    }

    /// <inheritdoc />
    public async Task<VerifiedCredential?> VerifyRegistrationAsync(string optionsJson, string responseJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(optionsJson);
        ArgumentNullException.ThrowIfNull(responseJson);
        VerifiedCredential? verified = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegisteredPublicKeyCredential credential = await _fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = JsonSerializer.Deserialize(responseJson, PasskeyJson.Default.AuthenticatorAttestationRawResponse)!,
                    OriginalOptions = CredentialCreateOptions.FromJson(optionsJson),
                    // Whether the credential is already kept is the store's to say, by its unique index, when it is added.
                    IsCredentialIdUniqueToUserCallback = static (_, _) => Task.FromResult(true),
                },
                cancellationToken).ConfigureAwait(false);
            verified = new VerifiedCredential(credential.Id, credential.PublicKey, credential.SignCount);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not JSON, not an attestation, or one that does not answer these options from this site.
        }

        return verified;
    }

    /// <inheritdoc />
    public string SignInOptions() =>
        _fido2.GetAssertionOptions(new GetAssertionOptionsParams { AllowedCredentials = [], UserVerification = UserVerificationRequirement.Required }).ToJson();

    /// <inheritdoc />
    public byte[]? CredentialOf(string responseJson)
    {
        ArgumentNullException.ThrowIfNull(responseJson);
        byte[]? id = null;
        try
        {
            id = JsonSerializer.Deserialize(responseJson, PasskeyJson.Default.AuthenticatorAssertionRawResponse)?.RawId;
        }
        catch (JsonException)
        {
            // Not an assertion.
        }

        return id is { Length: > 0 } ? id : null;
    }

    /// <inheritdoc />
    public async Task<VerifiedAssertion?> VerifySignInAsync(string optionsJson, string responseJson, Passkey passkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(optionsJson);
        ArgumentNullException.ThrowIfNull(responseJson);
        ArgumentNullException.ThrowIfNull(passkey);
        VerifiedAssertion? verified = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] owner = UserHandle(passkey.AccountId);
            VerifyAssertionResult result = await _fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = JsonSerializer.Deserialize(responseJson, PasskeyJson.Default.AuthenticatorAssertionRawResponse)!,
                    OriginalOptions = AssertionOptions.FromJson(optionsJson),
                    StoredPublicKey = passkey.PublicKey,
                    StoredSignatureCounter = (uint)passkey.SignCount,
                    // The handle the authenticator stored with the credential must be the account the credential is kept for.
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(owner)),
                },
                cancellationToken).ConfigureAwait(false);
            verified = new VerifiedAssertion(result.CredentialId, result.SignCount);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Another challenge, another site, another key, a counter that went backwards, or no user verification.
        }

        return verified;
    }
}

/// <summary>Serialisation for the two responses a browser sends, without reflection, as the trimming analyzers require.</summary>
[JsonSerializable(typeof(AuthenticatorAttestationRawResponse))]
[JsonSerializable(typeof(AuthenticatorAssertionRawResponse))]
internal sealed partial class PasskeyJson : JsonSerializerContext;
