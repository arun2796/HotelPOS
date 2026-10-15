using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Menu;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HotelPOS.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Folder for uploaded files (menu pictures go in its "menu" subfolder). Back it up with the database.</summary>
    public string RootPath { get; set; } = string.Empty;

    public string MenuImagesPath => Path.Combine(
        string.IsNullOrWhiteSpace(RootPath) ? Path.Combine(AppContext.BaseDirectory, "media") : RootPath,
        "menu");
}

/// <summary>
/// Stores menu pictures as JPEG, at most 512 px on the long side, upright (EXIF orientation applied) and on a
/// white background. Each upload gets a new file name so terminals that cache by URL never show a stale picture.
/// </summary>
public sealed class MenuImageStore : IMenuImageStore
{
    private const int JpegQuality = 85;

    private readonly MediaOptions _options;
    private readonly ILogger<MenuImageStore> _logger;

    public MenuImageStore(IOptions<MediaOptions> options, ILogger<MenuImageStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(int menuItemId, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var data = SKData.Create(buffer);
        using var codec = data is null ? null : SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        var scale = Math.Min(1.0, (double)MenuLimits.ImageMaxPixels / Math.Max(upright.Width, upright.Height));
        var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
        var height = Math.Max(1, (int)Math.Round(upright.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using (var source = SKImage.FromBitmap(upright))
        {
            canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using var image = surface.Snapshot();
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var directory = _options.MenuImagesPath;
        Directory.CreateDirectory(directory);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"{menuItemId}-{Guid.NewGuid().ToString("N")[..8]}.jpg");
        await using (var file = File.Create(Path.Combine(directory, fileName)))
        {
            jpeg.SaveTo(file);
        }

        _logger.LogInformation("Stored picture {File} ({Width}x{Height}) for menu item {ItemId}", fileName, width, height, menuItemId);
        return fileName;
    }

    public void Delete(string fileName)
    {
        // Only a bare file name inside the menu folder: never a path supplied from outside.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(_options.MenuImagesPath, safeName));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete picture {File}", safeName);
        }
    }

    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var (rotate, swap) = origin switch
        {
            SKEncodedOrigin.BottomRight => (180f, false),
            SKEncodedOrigin.RightTop => (90f, true),
            SKEncodedOrigin.LeftBottom => (270f, true),
            _ => (0f, false),
        };

        if (rotate == 0f)
        {
            return bitmap.Copy();
        }

        var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(rotate);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }
}
