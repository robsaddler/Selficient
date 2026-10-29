using Microsoft.AspNetCore.Razor.TagHelpers;
using Selficient.Web.Security;

namespace Selficient.Web.Tests.Security;

public sealed class CspNonceTagHelperTests
{
    [Fact]
    public void Stamps_the_request_nonce_on_the_element()
    {
        CspNonce nonce = new();
        CspNonceTagHelper helper = new(nonce);
        TagHelperContext context = new([], new Dictionary<object, object>(), "id");
        TagHelperOutput output = new("script", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(context, output);

        output.Attributes["nonce"].Value.ShouldBe(nonce.Value);
    }

    [Fact]
    public void Rejects_a_missing_output()
    {
        CspNonceTagHelper helper = new(new CspNonce());
        TagHelperContext context = new([], new Dictionary<object, object>(), "id");

        Should.Throw<ArgumentNullException>(() => helper.Process(context, null!));
    }

    [Fact]
    public void Nonces_are_unpredictable_and_well_formed()
    {
        HashSet<string> seen = [];
        for (int i = 0; i < 100; i++)
        {
            string value = new CspNonce().Value;
            System.Buffers.Text.Base64Url.DecodeFromChars(value).Length.ShouldBe(16);
            seen.Add(value).ShouldBeTrue();
        }
    }
}
