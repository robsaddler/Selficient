using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Selficient.Web.Security;

/// <summary>Stamps the request nonce on every script and style element the server renders.</summary>
[HtmlTargetElement("script")]
[HtmlTargetElement("style")]
public sealed class CspNonceTagHelper(CspNonce nonce) : TagHelper
{
    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Attributes.SetAttribute("nonce", nonce.Value);
    }
}
