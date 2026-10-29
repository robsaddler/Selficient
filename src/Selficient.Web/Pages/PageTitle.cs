namespace Selficient.Web.Pages;

/// <summary>Composes the document title so the layout carries no logic of its own.</summary>
public static class PageTitle
{
    private const string Product = "Selficient";

    /// <summary>"Page · Selficient" when a page names itself, otherwise just the product.</summary>
    public static string Compose(object? pageTitle) =>
        pageTitle is string title && !string.IsNullOrWhiteSpace(title)
            ? $"{title} · {Product}"
            : Product;
}
