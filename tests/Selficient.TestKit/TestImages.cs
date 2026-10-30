using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Selficient.TestKit;

/// <summary>Images for the sanitiser and the photo pages, built in memory.</summary>
public static class TestImages
{
    /// <summary>A plain JPEG of the given size with a lantern square in the top-left corner.</summary>
    public static byte[] Jpeg(int width = 640, int height = 480)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(new SKColor(0xF1, 0xF4, 0xF4));
            canvas.DrawRect(0, 0, width / 4f, height / 4f, new SKPaint { Color = new SKColor(0xE0, 0xA4, 0x58) });
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>A plain PNG.</summary>
    public static byte[] Png(int width = 320, int height = 200)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(new SKColor(0x1F, 0x5F, 0x4A));
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A JPEG with an APP1 segment that says Exif and an orientation of 6 (rotate 90° clockwise).</summary>
    public static byte[] JpegWithExifRotation(int width = 400, int height = 200)
    {
        byte[] plain = Jpeg(width, height);
        // Minimal Exif: TIFF header (little-endian), one IFD with the Orientation tag (0x0112) = 6.
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // II, 42, IFD at 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, // Orientation, SHORT, 1, 6
            0x00, 0x00, 0x00, 0x00,                         // next IFD: none
        ];
        byte[] exif = [.. Encoding.ASCII.GetBytes("Exif\0\0"), .. tiff];
        byte[] segment = new byte[4 + exif.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(exif.Length + 2));
        exif.CopyTo(segment, 4);
        // Insert straight after SOI (FF D8).
        return [plain[0], plain[1], .. segment, .. plain[2..]];
    }

    /// <summary>A palette PNG with no palette table: the codec opens it from the header and the decode fails.</summary>
    public static byte[] CorruptPng()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), 16);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 16);
        ihdr[8] = 8;
        ihdr[9] = 3; // palette, but no PLTE chunk follows
        byte[] garbage = [.. Enumerable.Range(0, 64).Select(static i => (byte)(255 - i))];
        return [.. signature, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", garbage), .. Chunk("IEND", [])];
    }

    /// <summary>A valid PNG with a script and a zip signature appended after IEND.</summary>
    public static byte[] Polyglot()
    {
        byte[] png = Png();
        byte[] tail = Encoding.ASCII.GetBytes("<script>alert(1)</script>PK");
        return [.. png, .. tail];
    }

    /// <summary>A PNG whose header declares a hundred thousand pixels a side and whose data is a few bytes.</summary>
    public static byte[] Bomb()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), 100_000);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 100_000);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type RGB
        return [.. signature, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]), .. Chunk("IEND", [])];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        byte[] crcInput = [.. typeBytes, .. data];
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        return [.. length, .. typeBytes, .. data, .. crc];
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>True when the bytes contain a JPEG APP1 (Exif) segment.</summary>
    public static bool HasExifSegment(ReadOnlySpan<byte> jpeg)
    {
        for (int i = 0; i + 1 < jpeg.Length; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xE1)
            {
                return true;
            }
        }

        return false;
    }
}
