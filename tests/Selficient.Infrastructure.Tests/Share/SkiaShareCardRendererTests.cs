using Selficient.Application.Share;
using Selficient.Infrastructure.Share;
using SkiaSharp;

namespace Selficient.Infrastructure.Tests.Share;

public sealed class SkiaShareCardRendererTests
{
    [Fact]
    public void The_card_is_a_chat_sized_png_on_the_night_with_the_lantern_lit()
    {
        SkiaShareCardRenderer renderer = new();

        byte[] png = renderer.Render(new ShareCard(1m, "storm", "A storm takes the power out"));
        byte[] other = renderer.Render(new ShareCard(7.5m, "water", "The water stops"));

        png[..8].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "a PNG signature");
        using SKBitmap bitmap = SKBitmap.Decode(png);
        bitmap.Width.ShouldBe(1200);
        bitmap.Height.ShouldBe(630);
        SKColor corner = bitmap.GetPixel(4, 4);
        (corner.Red < 0x30 && corner.Green < 0x40 && corner.Blue < 0x50).ShouldBeTrue($"the ground is the night, not {corner}");
        SKColor flame = bitmap.GetPixel(930, 330);
        (flame.Red > 0xC0 && flame.Red > flame.Blue + 0x60).ShouldBeTrue($"the lantern is lit at its centre, not {flame}");
        png.ShouldNotBe(other, "a different figure draws a different card");
        Should.Throw<ArgumentNullException>(() => renderer.Render(null!));
    }

    [Fact]
    public void The_card_knows_its_own_words()
    {
        ShareCard one = new(1m, "storm", "A storm takes the power out");
        ShareCard half = new(0.5m, "storm", "A storm takes the power out");
        ShareCard many = new(7.5m, "water", "The water stops");

        one.Figure.ShouldBe("1");
        one.Unit.ShouldBe("day");
        one.Clause.ShouldBe("when a storm takes the power out");
        half.Figure.ShouldBe("0.5");
        half.Unit.ShouldBe("days");
        many.Figure.ShouldBe("7.5");
        many.Clause.ShouldBe("when the water stops");
        ShareCard.Width.ShouldBe(1200);
        ShareCard.Height.ShouldBe(630);
    }
}
