using Selficient.Application.Ai;
using Selficient.Domain.Primitives;
using SkiaSharp;

namespace Selficient.Infrastructure.Images;

/// <summary>
/// Every upload is decoded by SkiaSharp (MIT) and re-encoded as a JPEG no larger than 1600 pixels on
/// its long edge. Decoding and re-encoding keeps pixels and nothing else: no EXIF, no GPS, no
/// colour profile, no trailing bytes, no second file hiding in the first. The codec decides what
/// the bytes are; the name and the declared content type are never consulted. Orientation from the
/// camera is applied before the metadata that carried it is dropped.
/// </summary>
public sealed class SkiaImageSanitiser : IImageSanitiser
{
    /// <summary>Longest edge of the re-encoded image.</summary>
    public const int MaxEdge = 1600;

    /// <summary>JPEG quality of the re-encoded image.</summary>
    public const int Quality = 80;

    /// <inheritdoc />
    public Result<SanitisedImage> Sanitise(ReadOnlyMemory<byte> upload)
    {
        if (upload.Length > IImageSanitiser.MaxUploadBytes)
        {
            return Result.Failure<SanitisedImage>(DomainErrors.ImageTooLarge());
        }

        if (upload.Length == 0)
        {
            return Result.Failure<SanitisedImage>(DomainErrors.ImageInvalid());
        }

        using SKData data = SKData.CreateCopy(upload.Span);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec is null)
        {
            return Result.Failure<SanitisedImage>(DomainErrors.ImageInvalid());
        }

        SKImageInfo info = codec.Info;
        if ((long)info.Width * info.Height > IImageSanitiser.MaxPixels)
        {
            return Result.Failure<SanitisedImage>(DomainErrors.ImageTooLarge());
        }

        using SKBitmap? decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return Result.Failure<SanitisedImage>(DomainErrors.ImageInvalid());
        }

        using SKBitmap upright = Orient(decoded, codec.EncodedOrigin);
        using SKBitmap sized = Shrink(upright);
        using SKImage image = SKImage.FromBitmap(sized);
        using SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, Quality);
        return Result.Success(new SanitisedImage(jpeg.ToArray(), sized.Width, sized.Height));
    }

    /// <summary>Applies the camera's orientation tag, since the tag itself will not survive.</summary>
    internal static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);
        (int degrees, bool swap) = origin switch
        {
            SKEncodedOrigin.RightTop => (90, true),
            SKEncodedOrigin.BottomRight => (180, false),
            SKEncodedOrigin.LeftBottom => (270, true),
            _ => (0, false),
        };

        if (degrees == 0)
        {
            return source.Copy();
        }

        SKBitmap rotated = new(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using SKCanvas canvas = new(rotated);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return rotated;
    }

    /// <summary>Scales so the long edge is at most <see cref="MaxEdge"/>; smaller images pass through.</summary>
    internal static SKBitmap Shrink(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int longest = Math.Max(source.Width, source.Height);
        if (longest <= MaxEdge)
        {
            return source.Copy();
        }

        float scale = (float)MaxEdge / longest;
        SKImageInfo target = new(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
