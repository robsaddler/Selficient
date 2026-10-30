using System.Text;
using Selficient.Application.Ai;
using Selficient.Domain.Primitives;
using Selficient.Infrastructure.Images;
using Selficient.TestKit;
using SkiaSharp;

namespace Selficient.Infrastructure.Tests.Images;

public sealed class SkiaImageSanitiserTests
{
    private readonly SkiaImageSanitiser _sanitiser = new();

    [Fact]
    public void A_plain_photo_comes_back_as_a_bounded_jpeg()
    {
        Result<SanitisedImage> result = _sanitiser.Sanitise(TestImages.Jpeg(4000, 3000));

        result.Value.Width.ShouldBe(1600);
        result.Value.Height.ShouldBe(1200);
        result.Value.Jpeg.Span[0].ShouldBe((byte)0xFF);
        result.Value.Jpeg.Span[1].ShouldBe((byte)0xD8);
        result.Value.Jpeg.Length.ShouldBeLessThan(1024 * 1024);
        _sanitiser.Sanitise(TestImages.Jpeg(640, 480)).Value.Width.ShouldBe(640, "a small photo is not enlarged");
    }

    [Fact]
    public void A_polyglot_is_reduced_to_its_pixels()
    {
        byte[] polyglot = TestImages.Polyglot();
        Encoding.ASCII.GetString(polyglot).ShouldContain("<script>");

        Result<SanitisedImage> result = _sanitiser.Sanitise(polyglot);

        result.IsSuccess.ShouldBeTrue();
        Encoding.ASCII.GetString(result.Value.Jpeg.ToArray()).ShouldNotContain("<script>");
        Encoding.ASCII.GetString(result.Value.Jpeg.ToArray()).ShouldNotContain("PK");
        result.Value.Width.ShouldBe(320);
    }

    [Fact]
    public void Metadata_is_stripped_and_orientation_applied()
    {
        byte[] rotated = TestImages.JpegWithExifRotation(400, 200);
        TestImages.HasExifSegment(rotated).ShouldBeTrue();

        Result<SanitisedImage> result = _sanitiser.Sanitise(rotated);

        TestImages.HasExifSegment(result.Value.Jpeg.Span).ShouldBeFalse();
        result.Value.Width.ShouldBe(200, "a 90 degree orientation swaps the sides");
        result.Value.Height.ShouldBe(400);
        Encoding.ASCII.GetString(result.Value.Jpeg.ToArray()).ShouldNotContain("Exif");
    }

    [Fact]
    public void Every_orientation_is_handled()
    {
        using SKBitmap source = new(30, 10);
        using (SKCanvas canvas = new(source))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawRect(0, 0, 5, 5, new SKPaint { Color = SKColors.Black });
        }

        using SKBitmap top = SkiaImageSanitiser.Orient(source, SKEncodedOrigin.TopLeft);
        using SKBitmap right = SkiaImageSanitiser.Orient(source, SKEncodedOrigin.RightTop);
        using SKBitmap bottom = SkiaImageSanitiser.Orient(source, SKEncodedOrigin.BottomRight);
        using SKBitmap left = SkiaImageSanitiser.Orient(source, SKEncodedOrigin.LeftBottom);

        (top.Width, top.Height).ShouldBe((30, 10));
        (right.Width, right.Height).ShouldBe((10, 30));
        (bottom.Width, bottom.Height).ShouldBe((30, 10));
        (left.Width, left.Height).ShouldBe((10, 30));
        top.GetPixel(2, 2).ShouldBe(SKColors.Black);
        bottom.GetPixel(27, 7).ShouldBe(SKColors.Black, "180 degrees puts the corner square at the far end");
        right.GetPixel(7, 2).ShouldBe(SKColors.Black, "90 degrees clockwise puts the top-left corner at the top-right");
        left.GetPixel(2, 27).ShouldBe(SKColors.Black, "90 degrees anticlockwise puts it at the bottom-left");
        Should.Throw<ArgumentNullException>(() => SkiaImageSanitiser.Orient(null!, SKEncodedOrigin.TopLeft));
        Should.Throw<ArgumentNullException>(() => SkiaImageSanitiser.Shrink(null!));
    }

    [Fact]
    public void Oversized_and_bomb_uploads_are_refused_before_decoding()
    {
        byte[] tooManyBytes = new byte[IImageSanitiser.MaxUploadBytes + 1];

        _sanitiser.Sanitise(tooManyBytes).Error.Code.ShouldBe("image.too-large");
        _sanitiser.Sanitise(TestImages.Bomb()).Error.Code.ShouldBe("image.too-large");
        _sanitiser.Sanitise(TestImages.Bomb()).Error.Message.ShouldBe("That photo is too large. Anything up to 10 MB and 24 megapixels is fine.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an image at all, however you name it")]
    [InlineData("%PDF-1.7 some pdf")]
    public void Not_an_image_is_a_sentence(string text)
    {
        Result<SanitisedImage> result = _sanitiser.Sanitise(Encoding.ASCII.GetBytes(text));

        result.Error.Code.ShouldBe("image.invalid");
        result.Error.Message.ShouldBe("That file is not a photo we can read. Use a JPEG, PNG or WebP straight from the camera.");
    }

    [Fact]
    public void A_truncated_or_corrupt_image_is_refused_rather_than_half_decoded()
    {
        byte[] jpeg = TestImages.Jpeg(640, 480);
        byte[] header = jpeg[..40];

        _sanitiser.Sanitise(header).Error.Code.ShouldBe("image.invalid");
        _sanitiser.Sanitise(TestImages.CorruptPng()).Error.Code.ShouldBe("image.invalid");
    }
}
