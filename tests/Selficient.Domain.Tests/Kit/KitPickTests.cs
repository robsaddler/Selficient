using Selficient.Domain.Kit;
using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Kit;

public sealed class KitPickTests
{
    private static readonly Uri Https = new("https://www.argos.co.uk/");
    private static readonly DateOnly Seen = new(2026, 10, 29);

    [Fact]
    public void A_valid_pick_is_trimmed_and_priced_in_pence()
    {
        KitPick pick = KitPick.Create("headtorches", "  Petzl Tikkina ", " Amazon ", 21.99m, Seen, Https, " One each. ").Value;

        pick.Action.Value.ShouldBe("headtorches");
        pick.Name.ShouldBe("Petzl Tikkina");
        pick.Retailer.ShouldBe("Amazon");
        pick.Price.Pence.ShouldBe(2199);
        pick.Seen.ShouldBe(Seen);
        pick.Link.ShouldBe(Https);
        pick.Note.ShouldBe("One each.");
        KitPick.Create("headtorches", "Name", "Shop", 1m, Seen, Https, null).Value.Note.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://www.argos.co.uk/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("ftp://example.com/")]
    public void Picks_refuse_anything_but_https(string link)
    {
        Result<KitPick> result = KitPick.Create("headtorches", "Name", "Shop", 1m, Seen, new Uri(link), null);

        result.Error.Code.ShouldBe("kit.link");
    }

    [Fact]
    public void Picks_refuse_a_missing_or_relative_link()
    {
        KitPick.Create("headtorches", "Name", "Shop", 1m, Seen, null, null).Error.Code.ShouldBe("kit.link");
        KitPick.Create("headtorches", "Name", "Shop", 1m, Seen, new Uri("shop", UriKind.Relative), null).Error.Code.ShouldBe("kit.link");
    }

    [Theory]
    [InlineData("", "Shop", "")]
    [InlineData("Name", " ", "")]
    [InlineData("Name", "Shop", "note that is far too long to show anyone, repeated so that it exceeds the limit of one hundred and sixty characters which is the cap for every piece of catalogue text on the page")]
    public void Picks_refuse_missing_or_overlong_text(string name, string retailer, string note)
    {
        Result<KitPick> result = KitPick.Create("headtorches", name, retailer, 1m, Seen, Https, note.Length == 0 ? null : note);

        result.Error.Code.ShouldBe("kit.text");
    }

    [Fact]
    public void Picks_refuse_a_bad_action_id_or_a_negative_price()
    {
        KitPick.Create("Not An Id", "Name", "Shop", 1m, Seen, Https, null).Error.Code.ShouldBe("kit.action");
        KitPick.Create("headtorches", "Name", "Shop", -1m, Seen, Https, null).Error.Code.ShouldBe("quantity.negative");
    }
}
