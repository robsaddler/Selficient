using System.Globalization;
using System.Text;
using QRCoder;

namespace Selficient.Web.Formatting;

/// <summary>
/// A QR code as one SVG path, drawn inline on the page. No image is fetched, nothing is written to
/// disk and the enrolment address never leaves the response it is part of, which matters because
/// that address carries the authenticator's seed.
/// </summary>
/// <param name="Path">The dark modules as a single SVG path, in module units.</param>
/// <param name="Modules">The width of the code in modules, which is the SVG's viewBox.</param>
public readonly record struct QrCode(string Path, int Modules)
{
    /// <summary>
    /// Encodes text at the given error correction. Medium is what authenticator apps expect and
    /// keeps the code small enough to scan from a laptop screen.
    /// </summary>
    public static QrCode For(string text, QRCodeGenerator.ECCLevel correction = QRCodeGenerator.ECCLevel.M)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(text, correction);

        // QRCoder pads the matrix with a four-module quiet zone; it is kept, because a code without
        // one is unreliable to scan, and the SVG is sized to include it.
        int modules = data.ModuleMatrix.Count;
        StringBuilder path = new();
        for (int row = 0; row < modules; row++)
        {
            System.Collections.BitArray line = data.ModuleMatrix[row];
            int column = 0;
            while (column < modules)
            {
                if (!line[column])
                {
                    column++;
                    continue;
                }

                // Each run of dark modules on a row becomes one horizontal bar, so the path is a
                // few hundred commands rather than a few thousand rectangles.
                int start = column;
                while (column < modules && line[column])
                {
                    column++;
                }

                path.Append(CultureInfo.InvariantCulture, $"M{start} {row}h{column - start}v1h-{column - start}z");
            }
        }

        return new QrCode(path.ToString(), modules);
    }
}
