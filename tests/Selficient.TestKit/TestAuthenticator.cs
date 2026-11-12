using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Selficient.TestKit;

/// <summary>
/// A passkey authenticator in software: it makes a P-256 key, answers a registration with an
/// unattested credential and a sign-in with a signed assertion, as a phone's secure chip does. It
/// stands in for the device so the ceremonies are proved against real signatures and real CBOR,
/// and it can be made to misbehave in each of the ways the threat model lists.
/// </summary>
public sealed class TestAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[] _userHandle = [];

    /// <summary>Creates an authenticator that believes it is on a page of the given origin.</summary>
    public TestAuthenticator(string origin = "https://localhost")
    {
        Origin = origin;
    }

    /// <summary>The origin it writes into what it signs.</summary>
    public string Origin { get; set; }

    /// <summary>The id of the one credential it holds.</summary>
    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>Its signature counter. Goes up by one with every assertion.</summary>
    public uint Counter { get; set; }

    /// <summary>Whether it says the person was verified: a fingerprint, a face, a PIN.</summary>
    public bool VerifiesUser { get; set; } = true;

    /// <summary>The relying party it claims in what it signs, in place of the one it was asked for. For playing a phishing site.</summary>
    public string? ClaimsRelyingParty { get; set; }

    /// <summary>Answers registration options with a new, unattested credential.</summary>
    public string Create(string optionsJson)
    {
        using JsonDocument options = JsonDocument.Parse(optionsJson);
        string challenge = options.RootElement.GetProperty("challenge").GetString()!;
        string relyingParty = ClaimsRelyingParty ?? options.RootElement.GetProperty("rp").GetProperty("id").GetString()!;
        _userHandle = Base64Url.DecodeFromChars(options.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        byte[] clientData = ClientData("webauthn.create", challenge);

        CborWriter cose = new(CborConformanceMode.Ctap2Canonical);
        ECParameters parameters = _key.ExportParameters(includePrivateParameters: false);
        cose.WriteStartMap(5);
        cose.WriteInt32(1);
        cose.WriteInt32(2);
        cose.WriteInt32(3);
        cose.WriteInt32(-7);
        cose.WriteInt32(-1);
        cose.WriteInt32(1);
        cose.WriteInt32(-2);
        cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3);
        cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();

        byte[] length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
        byte[] authenticatorData = [.. AuthenticatorData(relyingParty, attested: true), .. new byte[16], .. length, .. CredentialId, .. cose.Encode()];

        CborWriter attestation = new(CborConformanceMode.Lax);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new Dictionary<string, object>
            {
                ["attestationObject"] = Base64Url.EncodeToString(attestation.Encode()),
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["transports"] = new[] { "internal" },
            },
            ["clientExtensionResults"] = new Dictionary<string, object>(),
        });
    }

    /// <summary>Answers sign-in options with an assertion signed by its key.</summary>
    public string Assert(string optionsJson)
    {
        using JsonDocument options = JsonDocument.Parse(optionsJson);
        string challenge = options.RootElement.GetProperty("challenge").GetString()!;
        string relyingParty = ClaimsRelyingParty ?? options.RootElement.GetProperty("rpId").GetString()!;
        byte[] clientData = ClientData("webauthn.get", challenge);
        Counter++;
        byte[] authenticatorData = AuthenticatorData(relyingParty, attested: false);
        byte[] signature = _key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new Dictionary<string, object>
            {
                ["authenticatorData"] = Base64Url.EncodeToString(authenticatorData),
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = Base64Url.EncodeToString(_userHandle),
            },
            ["clientExtensionResults"] = new Dictionary<string, object>(),
        });
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes($$"""{"type":"{{type}}","challenge":"{{challenge}}","origin":"{{Origin}}","crossOrigin":false}""");

    /// <summary>The hash of the relying party, the flags and the counter: the part of what is signed that the authenticator itself vouches for.</summary>
    private byte[] AuthenticatorData(string relyingParty, bool attested)
    {
        const byte UserPresent = 0x01;
        const byte UserVerified = 0x04;
        const byte AttestedCredential = 0x40;
        byte flags = (byte)(UserPresent | (VerifiesUser ? UserVerified : 0) | (attested ? AttestedCredential : 0));
        byte[] counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, Counter);
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(relyingParty)), flags, .. counter];
    }
}
