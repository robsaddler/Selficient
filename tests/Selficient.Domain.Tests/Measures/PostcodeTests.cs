using Selficient.Domain.Measures;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Measures;

public sealed class PostcodeTests
{
    [Theory]
    [InlineData("NP12 1GW", "NP12 1GW", "NP12", "NP")]
    [InlineData("np121gw", "NP12 1GW", "NP12", "NP")]
    [InlineData("  sw1a  1aa ", "SW1A 1AA", "SW1A", "SW")]
    [InlineData("M1 1AE", "M1 1AE", "M1", "M")]
    [InlineData("GIR 0AA", "GIR 0AA", "GIR", "GIR")]
    [InlineData("EC1A1BB", "EC1A 1BB", "EC1A", "EC")]
    [InlineData("BT1 5GS", "BT1 5GS", "BT1", "BT")]
    public void Normalises_case_and_spacing(string raw, string expected, string outcode, string area)
    {
        Postcode postcode = Postcode.Create(raw).Value;

        postcode.Value.ShouldBe(expected);
        postcode.Outcode.ShouldBe(outcode);
        postcode.Area.ShouldBe(area);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NP12")]
    [InlineData("NOTAPOSTCODE")]
    [InlineData("12345")]
    [InlineData("NP12 1GW; DROP TABLE")]
    [InlineData("../../etc/passwd")]
    [InlineData("NP12 1GW%2F..")]
    [InlineData("QZ12 1GW")]
    [InlineData("NP12 1CW")]
    [InlineData("this is far too long to be a postcode at all")]
    public void Postcode_refuses_anything_that_is_not_a_uk_postcode(string? raw)
    {
        Result<Postcode> result = Postcode.Create(raw);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("postcode.invalid");
        result.Error.Message.ShouldBe("That does not look like a UK postcode. Check it and try again.");
    }
}
