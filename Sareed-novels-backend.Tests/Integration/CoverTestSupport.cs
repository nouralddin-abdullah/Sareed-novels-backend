using System.Buffers.Binary;
using System.Collections.Concurrent;
using Application.Services;
using Infrastructure.Services.Storage;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>A bucket in memory, with the same key/URL rules as the R2 service.</summary>
public sealed class InMemoryObjectStorage(string publicBaseUrl) : IObjectStorage
{
    public ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Objects { get; } = new();

    public int Puts;

    public string PublicBaseUrl => publicBaseUrl;

    public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Objects[key] = (copy.ToArray(), contentType);
        Interlocked.Increment(ref Puts);
        return StorageKeys.PublicUrl(publicBaseUrl, key);
    }

    public Task<byte[]?> GetAsync(string keyOrUrl, long maxBytes, CancellationToken cancellationToken = default)
    {
        var key = KeyOf(keyOrUrl);
        if (key is null || !Objects.TryGetValue(key, out var stored)) return Task.FromResult<byte[]?>(null);
        if (stored.Bytes.Length > maxBytes) throw new InvalidDataException("Object over the limit.");
        return Task.FromResult<byte[]?>(stored.Bytes);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public string? KeyOf(string keyOrUrl) => StorageKeys.KeyFrom(publicBaseUrl, keyOrUrl);

    /// <summary>Puts a file the way legacy covers were stored (raw key, possibly with spaces) and returns its stored URL.</summary>
    public string AddLegacy(string key, byte[] bytes, string contentType = "image/png")
    {
        Objects[key] = (bytes, contentType);
        return $"{publicBaseUrl}/{key}";
    }
}

/// <summary>Synthetic images for cover tests: solid colour blocks, so where each part lands is easy to check.</summary>
public static class TestImages
{
    /// <summary>An image split into a left and a right half (or top/bottom) of two colours.</summary>
    public static byte[] Halves(int width, int height, SKColor first, SKColor second, SKEncodedImageFormat format,
        bool vertical = false, int quality = 90)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        using var a = new SKPaint { Color = first };
        using var b = new SKPaint { Color = second };
        if (vertical)
        {
            canvas.DrawRect(0, 0, width, height / 2f, a);
            canvas.DrawRect(0, height / 2f, width, height - height / 2f, b);
        }
        else
        {
            canvas.DrawRect(0, 0, width / 2f, height, a);
            canvas.DrawRect(width / 2f, 0, width - width / 2f, height, b);
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>
    /// A canvas of <paramref name="bar"/> colour with artwork (top half <paramref name="first"/>, bottom half
    /// <paramref name="second"/>) in <paramref name="artwork"/>: a letterboxed or pillarboxed image.
    /// </summary>
    public static byte[] Boxed(int width, int height, SKRect artwork, SKColor bar, SKColor first, SKColor second,
        SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(bar);
        using var a = new SKPaint { Color = first };
        using var b = new SKPaint { Color = second };
        canvas.DrawRect(artwork.Left, artwork.Top, artwork.Width, artwork.Height / 2, a);
        canvas.DrawRect(artwork.Left, artwork.MidY, artwork.Width, artwork.Height / 2, b);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 92);
        return data.ToArray();
    }

    public static byte[] Solid(int width, int height, SKColor color, SKEncodedImageFormat format = SKEncodedImageFormat.Png) =>
        Halves(width, height, color, color, format);

    /// <summary>A PNG whose left half is fully transparent and right half opaque <paramref name="colour"/>.</summary>
    public static byte[] TransparentLeftHalf(int width, int height, SKColor colour)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = colour };
        surface.Canvas.DrawRect(width / 2f, 0, width / 2f, height, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Inserts an EXIF block (APP1) with the given orientation, a camera make and a GPS pointer into a JPEG, the way a
    /// phone photo carries them.
    /// </summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        // TIFF, little endian: header, one IFD with Orientation (0x0112), Make (0x010F) and GPSInfo (0x8825).
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(U16(42));
        tiff.AddRange(U32(8));
        const int entries = 3;
        var makeOffset = 8 + 2 + entries * 12 + 4;
        tiff.AddRange(U16(entries));
        tiff.AddRange(Entry(0x010F, 2, 6, (uint)makeOffset));         // Make, ASCII, 6 bytes at makeOffset
        tiff.AddRange(Entry(0x0112, 3, 1, orientation));               // Orientation, SHORT
        tiff.AddRange(Entry(0x8825, 4, 1, (uint)(makeOffset + 6)));   // GPS IFD pointer
        tiff.AddRange(U32(0));
        tiff.AddRange("Phone\0"u8.ToArray());
        tiff.AddRange(U16(0));                                         // empty GPS IFD
        tiff.AddRange(U32(0));

        var app1 = new List<byte> { 0xFF, 0xE1 };
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        app1.AddRange(U16BigEndian((ushort)(payload.Length + 2)));
        app1.AddRange(payload);

        return jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray();

        static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); return b; }
        static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
        static byte[] U16BigEndian(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
        static byte[] Entry(ushort tag, ushort type, uint count, uint value)
        {
            var e = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(e, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(4), count);
            if (type == 3) BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(8), (ushort)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(8), value);
            return e;
        }
    }

    /// <summary>A PNG whose header claims the given size (no real pixel data), to test limits without allocating.</summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(Chunk("IHDR", ihdr));
        bytes.AddRange(Chunk("IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]));
        bytes.AddRange(Chunk("IEND", []));
        return bytes.ToArray();

        static byte[] Chunk(string type, byte[] data)
        {
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var chunk = new byte[12 + data.Length];
            BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
            typeBytes.CopyTo(chunk, 4);
            data.CopyTo(chunk, 8);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(typeBytes.Concat(data).ToArray()));
            return chunk;
        }

        static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }

    public static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Not an image.");

    /// <summary>True when two colours are within <paramref name="tolerance"/> per channel (lossy encoders shift them a little).</summary>
    public static bool Near(SKColor actual, SKColor expected, int tolerance = 24) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance
        && Math.Abs(actual.Green - expected.Green) <= tolerance
        && Math.Abs(actual.Blue - expected.Blue) <= tolerance;
}
