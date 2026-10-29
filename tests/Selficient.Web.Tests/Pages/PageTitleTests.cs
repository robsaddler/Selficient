using Selficient.Web.Pages;

namespace Selficient.Web.Tests.Pages;

public sealed class PageTitleTests
{
    [Theory]
    [InlineData("Styleguide", "Styleguide · Selficient")]
    [InlineData(null, "Selficient")]
    [InlineData("", "Selficient")]
    [InlineData("   ", "Selficient")]
    [InlineData(42, "Selficient")]
    public void Composes_the_document_title(object? pageTitle, string expected)
    {
        PageTitle.Compose(pageTitle).ShouldBe(expected);
    }
}
