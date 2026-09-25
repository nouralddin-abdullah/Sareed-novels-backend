using Application.Covers;
using SkiaSharp;

namespace Infrastructure.Services.Covers;

/// <summary>
/// The 1200x630 link-preview image (og:image) for a novel, in the style readers know from Wattpad: the cover itself,
/// full height and centred with a soft shadow and rounded corners, on a heavily blurred and darkened copy of the same
/// cover, with the small semi-transparent Sard wordmark in the bottom-left corner (the site is right-to-left). No text
/// is drawn: the platform shows og:title and og:description under the image.
/// </summary>
public static class ShareImageRenderer
{
    public const int Width = NovelCovers.ShareImageWidth;
    public const int Height = NovelCovers.ShareImageHeight;

    /// <summary>Height of the centred cover (a 35 px margin above and below).</summary>
    public const int CoverHeight = 560;

    private const float CornerRadius = 10;
    private const float BackdropBlur = 36;
    private const byte BackdropShade = 92;       // black overlay, ~36%
    private const int WordmarkHeight = 46;
    private const int WordmarkMargin = 34;
    private const byte WordmarkAlpha = 200;      // ~78% white

    private static readonly Lazy<SKImage> Wordmark = new(LoadWordmark);

    /// <summary>Renders the share image for a 2:3 cover (any size; normally the 960 px master).</summary>
    public static SKImage Render(SKImage cover) => Render(cover, cover);

    private static SKImage Render(SKImage cover, SKImage backdrop)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("Could not allocate the share image surface.");
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(20, 20, 28));

        DrawBackdrop(canvas, backdrop);
        DrawCover(canvas, cover);
        DrawWordmark(canvas);

        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// The share image for a novel without a usable cover: a placeholder cover in Sard's colours carrying the
    /// wordmark, composed the same way (the web app ships it as public/og-default.jpg).
    /// </summary>
    public static SKImage RenderDefault()
    {
        using var placeholder = RenderPlaceholderCover(640);
        // The backdrop is the gradient alone: a blurred giant wordmark would only leave grey smudges.
        using var gradient = RenderPlaceholderCover(640, withWordmark: false);
        return Render(placeholder, gradient);
    }

    /// <summary>A 2:3 cover in the brand gradient with the wordmark in the middle.</summary>
    public static SKImage RenderPlaceholderCover(int width, bool withWordmark = true)
    {
        var height = NovelCovers.HeightFor(width);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("Could not allocate the placeholder surface.");
        var canvas = surface.Canvas;
        using (var gradient = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width * 0.35f, height),
                [new SKColor(0x0F, 0x34, 0x60), new SKColor(0x16, 0x21, 0x3E), new SKColor(0x1A, 0x1A, 0x2E)],
                [0f, 0.55f, 1f],
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), gradient);
        }

        // A thin inset frame, like a printed cover.
        using (var frame = new SKPaint { Color = SKColors.White.WithAlpha(40), IsStroke = true, StrokeWidth = Math.Max(1, width / 320f), IsAntialias = true })
        {
            var inset = width * 0.06f;
            canvas.DrawRect(new SKRect(inset, inset, width - inset, height - inset), frame);
        }

        if (!withWordmark)
        {
            canvas.Flush();
            return surface.Snapshot();
        }

        var markWidth = (int)(width * 0.5f);
        var markHeight = (int)Math.Round(markWidth * (double)Wordmark.Value.Height / Wordmark.Value.Width);
        using var mark = CoverImageProcessor.Resize(Wordmark.Value, markWidth, markHeight);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(235) };
        canvas.DrawImage(mark, (width - markWidth) / 2f, (height - markHeight) / 2f, paint);

        canvas.Flush();
        return surface.Snapshot();
    }

    private static void DrawBackdrop(SKCanvas canvas, SKImage cover)
    {
        // Blurring a small copy is as good as blurring a large one (all detail goes anyway) and much cheaper.
        var smallWidth = Math.Min(cover.Width, 240);
        using var small = CoverImageProcessor.Resize(cover, smallWidth, Math.Max(1, (int)Math.Round(smallWidth * (double)cover.Height / cover.Width)));
        var fill = Math.Max((float)Width / small.Width, (float)Height / small.Height);
        var drawnWidth = small.Width * fill;
        var drawnHeight = small.Height * fill;
        var destination = SKRect.Create((Width - drawnWidth) / 2, (Height - drawnHeight) / 2, drawnWidth, drawnHeight);

        using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(BackdropBlur, BackdropBlur, SKShaderTileMode.Clamp) })
        {
            canvas.SaveLayer(blur);
            canvas.DrawImage(small, destination, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            canvas.Restore();
        }

        using var shade = new SKPaint { Color = SKColors.Black.WithAlpha(BackdropShade) };
        canvas.DrawRect(new SKRect(0, 0, Width, Height), shade);
    }

    private static void DrawCover(SKCanvas canvas, SKImage cover)
    {
        var height = CoverHeight;
        var width = (int)Math.Round(height * (double)cover.Width / cover.Height);
        var rect = SKRect.Create((Width - width) / 2f, (Height - height) / 2f, width, height);
        var rounded = new SKRoundRect(rect, CornerRadius);

        using (var shadow = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(150),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16),
            IsAntialias = true
        })
        {
            var shadowRect = rect;
            shadowRect.Offset(0, 10);
            canvas.DrawRoundRect(new SKRoundRect(shadowRect, CornerRadius), shadow);
        }

        using var scaled = CoverImageProcessor.Resize(cover, width, height);
        canvas.Save();
        canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, antialias: true);
        canvas.DrawImage(scaled, rect.Left, rect.Top);
        canvas.Restore();

        // A hairline edge keeps dark covers from melting into the dark backdrop.
        using var edge = new SKPaint { Color = SKColors.White.WithAlpha(34), IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(rounded, edge);
    }

    private static void DrawWordmark(SKCanvas canvas)
    {
        var mark = Wordmark.Value;
        var width = (int)Math.Round(WordmarkHeight * (double)mark.Width / mark.Height);
        using var scaled = CoverImageProcessor.Resize(mark, width, WordmarkHeight);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(WordmarkAlpha) };
        canvas.DrawImage(scaled, WordmarkMargin, Height - WordmarkMargin - WordmarkHeight, paint);
    }

    private static SKImage LoadWordmark()
    {
        using var stream = typeof(ShareImageRenderer).Assembly.GetManifestResourceStream("Sard.Covers.Wordmark.png")
            ?? throw new InvalidOperationException("The Sard wordmark resource is missing from the build.");
        using var data = SKData.Create(stream);
        using var encoded = SKImage.FromEncodedData(data)
            ?? throw new InvalidOperationException("The Sard wordmark resource is not a readable PNG.");
        return encoded.ToRasterImage(ensurePixelData: true);
    }
}
