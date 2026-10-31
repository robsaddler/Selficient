using QRCoder;
using Selficient.Web.Formatting;

namespace Selficient.Web.Tests.Formatting;

/// <summary>
/// The QR code the enrolment page draws. The encoding itself is QRCoder's; what is pinned here is
/// that the path covers the right modules, that the quiet zone survives, and that a reader given
/// the path back can recover the matrix, which is the only honest way to say "this scans".
/// </summary>
public sealed class QrCodeTests
{
    [Fact]
    public void The_path_draws_exactly_the_dark_modules_of_the_code()
    {
        const string text = "otpauth://totp/Selficient%3Arob?secret=JBSWY3DPEHPK3PXP&issuer=Selficient&algorithm=SHA1&digits=6&period=30";
        using QRCodeGenerator generator = new();
        using QRCodeData expected = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        QrCode code = QrCode.For(text);

        code.Modules.ShouldBe(expected.ModuleMatrix.Count);
        bool[,] drawn = Read(code);
        for (int row = 0; row < code.Modules; row++)
        {
            for (int column = 0; column < code.Modules; column++)
            {
                drawn[row, column].ShouldBe(expected.ModuleMatrix[row][column], $"module {row},{column} differs from the encoder's own matrix");
            }
        }
    }

    [Fact]
    public void The_quiet_zone_is_kept_because_a_code_without_one_does_not_scan()
    {
        QrCode code = QrCode.For("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP");
        bool[,] drawn = Read(code);

        for (int edge = 0; edge < 4; edge++)
        {
            for (int along = 0; along < code.Modules; along++)
            {
                drawn[edge, along].ShouldBeFalse($"the top quiet zone is light at row {edge}");
                drawn[code.Modules - 1 - edge, along].ShouldBeFalse($"the bottom quiet zone is light at row {code.Modules - 1 - edge}");
                drawn[along, edge].ShouldBeFalse($"the left quiet zone is light at column {edge}");
                drawn[along, code.Modules - 1 - edge].ShouldBeFalse($"the right quiet zone is light at column {code.Modules - 1 - edge}");
            }
        }
    }

    [Fact]
    public void A_run_of_dark_modules_is_one_bar_rather_than_a_rectangle_each()
    {
        // The finder pattern's top edge is seven modules of solid dark, and it is drawn as one
        // command. A code drawn module by module is several thousand, which is a slow page.
        QrCode code = QrCode.For("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP");

        code.Path.ShouldContain("h7v1h-7z");
        code.Path.ShouldNotContain("h1v1h-1z" + "h1v1h-1z" + "h1v1h-1z", customMessage: "three single modules in a row would mean the runs are not being joined");
    }

    [Fact]
    public void A_higher_correction_makes_a_denser_code_and_nothing_is_encoded_from_nothing()
    {
        QrCode medium = QrCode.For("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP");
        QrCode high = QrCode.For("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP", QRCodeGenerator.ECCLevel.H);

        high.Modules.ShouldBeGreaterThan(medium.Modules, "more correction needs more room");
        Should.Throw<ArgumentException>(() => QrCode.For(" "));
        Should.Throw<ArgumentException>(() => QrCode.For(null!));
    }

    /// <summary>Reads the path back into a matrix, so the test checks what a scanner would see.</summary>
    private static bool[,] Read(QrCode code)
    {
        bool[,] matrix = new bool[code.Modules, code.Modules];
        foreach (System.Text.RegularExpressions.Match bar in System.Text.RegularExpressions.Regex.Matches(code.Path, @"M(?<x>\d+) (?<y>\d+)h(?<run>\d+)v1h-\k<run>z"))
        {
            int x = int.Parse(bar.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture);
            int y = int.Parse(bar.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture);
            int run = int.Parse(bar.Groups["run"].Value, System.Globalization.CultureInfo.InvariantCulture);
            for (int along = 0; along < run; along++)
            {
                matrix[y, x + along] = true;
            }
        }

        return matrix;
    }
}
