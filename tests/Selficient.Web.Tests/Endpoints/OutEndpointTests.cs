using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Selficient.Application.Catalogue;
using Selficient.TestKit;

namespace Selficient.Web.Tests.Endpoints;

public sealed class OutEndpointTests : IDisposable
{
    private readonly SelficientWebApplicationFactory _factory = new();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private async Task<(ICatalogueStore Store, Offer Offer)> SeededAsync()
    {
        await _factory.Services.GetRequiredService<CatalogueOperations>().ReloadAsync(CancellationToken.None);
        ICatalogueStore store = _factory.Services.GetRequiredService<ICatalogueStore>();
        return (store, (await store.ShownAsync(CancellationToken.None))[0]);
    }

    [Fact]
    public async Task A_shop_link_counts_the_click_and_sends_the_reader_on_without_being_cached()
    {
        (ICatalogueStore store, Offer offer) = await SeededAsync();
        using HttpClient client = Client();

        using HttpResponseMessage first = await client.GetAsync(new Uri($"/out/{offer.Id}", UriKind.Relative));
        using HttpResponseMessage second = await client.GetAsync(new Uri($"/out/{offer.Id}", UriKind.Relative));
        IReadOnlyDictionary<long, long> clicks = await store.ClicksAsync(1, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        first.StatusCode.ShouldBe(HttpStatusCode.Found);
        first.Headers.Location.ShouldBe(offer.Link);
        first.Headers.CacheControl!.NoStore.ShouldBeTrue("a counted hop is never served from a cache");
        first.Headers.Contains("Set-Cookie").ShouldBeFalse("no cookie follows the reader to the shop");
        second.StatusCode.ShouldBe(HttpStatusCode.Found);
        clicks[offer.Id].ShouldBe(2);
    }

    [Fact]
    public async Task A_hidden_proposed_or_unknown_offer_is_a_sentence_and_no_redirect()
    {
        (ICatalogueStore store, Offer offer) = await SeededAsync();
        Offer second = (await store.ShownAsync(CancellationToken.None))[1];
        await store.SetStatusAsync(offer.Id, OfferStatus.Hidden, CancellationToken.None);
        await store.SetStatusAsync(second.Id, OfferStatus.Proposed, CancellationToken.None);
        using HttpClient client = Client();

        using HttpResponseMessage hidden = await client.GetAsync(new Uri($"/out/{offer.Id}", UriKind.Relative));
        using HttpResponseMessage proposed = await client.GetAsync(new Uri($"/out/{second.Id}", UriKind.Relative));
        using HttpResponseMessage unknown = await client.GetAsync(new Uri("/out/987654321", UriKind.Relative));
        using HttpResponseMessage notANumber = await client.GetAsync(new Uri("/out/petzl", UriKind.Relative));
        using HttpResponseMessage posted = await client.PostAsync(new Uri($"/out/{offer.Id}", UriKind.Relative), new StringContent(""));

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).ShouldContain("That item is no longer in the catalogue. The plan will show another.");
        hidden.Headers.Location.ShouldBeNull();
        proposed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        notANumber.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        posted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await store.ClicksAsync(1, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None)).ShouldBeEmpty("nothing that did not send the reader on is counted");
    }

    public void Dispose() => _factory.Dispose();
}
