using Selficient.Application.Share;
using SkiaSharp;

namespace Selficient.Infrastructure.Share;

/// <summary>
/// The share card drawn with Skia: the night ground from the design direction, the lantern's glow,
/// the figure in lantern, and one line of invitation. The typeface is the system's bold sans, so the
/// card draws on the vhost and on the build runner without the site's web fonts, which are subsets
/// in a format Skia does not read.
/// </summary>
public sealed class SkiaShareCardRenderer : IShareCardRenderer
{
    private static readonly SKColor Night = new(0x10, 0x1C, 0x27);
    private static readonly SKColor Paper = new(0xE7, 0xEC, 0xEF);
    private static readonly SKColor Muted = new(0xA9, 0xB6, 0xBF);
    private static readonly SKColor Lantern = new(0xE8, 0xA3, 0x3D);
    private static readonly SKColor Pine = new(0x6F, 0xBA, 0x9A);

    /// <inheritdoc />
    public byte[] Render(ShareCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(ShareCard.Width, ShareCard.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(Night);
        DrawGlow(canvas);
        DrawWords(canvas, card);
        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static void DrawGlow(SKCanvas canvas)
    {
        SKPoint centre = new(930, 330);
        using SKShader shader = SKShader.CreateRadialGradient(
            centre,
            330,
            [Lantern.WithAlpha(0x9A), Lantern.WithAlpha(0x33), Night.WithAlpha(0)],
            [0f, 0.45f, 1f],
            SKShaderTileMode.Clamp);
        using SKPaint glow = new() { Shader = shader, IsAntialias = true };
        canvas.DrawCircle(centre, 330, glow);
        using SKPaint flame = new() { Color = Lantern, IsAntialias = true };
        canvas.DrawCircle(centre, 26, flame);
        using SKPaint ring = new() { Color = Paper.WithAlpha(0x55), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6 };
        canvas.DrawRoundRect(new SKRect(centre.X - 70, centre.Y - 110, centre.X + 70, centre.Y + 110), 22, 22, ring);
    }

    private static void DrawWords(SKCanvas canvas, ShareCard card)
    {
        using SKTypeface bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using SKTypeface regular = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        using SKFont eyebrow = new(bold, 28) { Subpixel = true };
        using SKFont heading = new(regular, 46) { Subpixel = true };
        using SKFont figure = new(bold, 300) { Subpixel = true };
        using SKFont unit = new(regular, 64) { Subpixel = true };
        using SKFont clause = new(regular, 40) { Subpixel = true };
        using SKFont footer = new(bold, 34) { Subpixel = true };
        using SKPaint lantern = new() { Color = Lantern, IsAntialias = true };
        using SKPaint paper = new() { Color = Paper, IsAntialias = true };
        using SKPaint muted = new() { Color = Muted, IsAntialias = true };
        using SKPaint pine = new() { Color = Pine, IsAntialias = true };

        const float left = 72;
        canvas.DrawText("SELFICIENT", left, 96, SKTextAlign.Left, eyebrow, lantern);
        canvas.DrawText("Our house could stand on its own for", left, 170, SKTextAlign.Left, heading, paper);
        canvas.DrawText(card.Figure, left - 8, 420, SKTextAlign.Left, figure, lantern);
        float figureWidth = figure.MeasureText(card.Figure);
        canvas.DrawText(card.Unit, left + figureWidth + 18, 420, SKTextAlign.Left, unit, paper);
        canvas.DrawText(card.Clause + ".", left, 492, SKTextAlign.Left, clause, muted);
        canvas.DrawText("How about yours? A minute of questions at selficient.app", left, 574, SKTextAlign.Left, footer, pine);
    }
}
