using System.Globalization;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Print;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HotelPOS.Infrastructure.Media;

/// <summary>
/// The restaurant logo for invoices and PDFs: a PNG at most 600 px on the long side, on white (thermal printers and
/// PDFs have no transparency to show), in the "branding" folder beside the menu pictures. Each upload gets a new
/// file name; the Settings row LogoFile points at the current one.
/// </summary>
public sealed class LogoStore : ILogoStore
{
    private readonly MediaOptions _options;
    private readonly ILogger<LogoStore> _logger;

    public LogoStore(IOptions<MediaOptions> options, ILogger<LogoStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private string Folder => Path.Combine(Path.GetDirectoryName(_options.MenuImagesPath)!, "branding");

    public async Task<string?> SaveAsync(Stream content, CancellationToken cancellationToken = default)
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

        var scale = Math.Min(1.0, (double)TemplateLimits.LogoMaxPixels / Math.Max(decoded.Width, decoded.Height));
        var width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        using (var source = SKImage.FromBitmap(decoded))
        {
            surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Folder);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"logo-{Guid.NewGuid().ToString("N")[..8]}.png");
        await using (var file = File.Create(Path.Combine(Folder, fileName)))
        {
            png.SaveTo(file);
        }

        _logger.LogInformation("Stored logo {File} ({Width}x{Height})", fileName, width, height);
        return fileName;
    }

    public byte[]? Read(string? fileName)
    {
        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(safeName))
        {
            return null;
        }

        var path = Path.Combine(Folder, safeName);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read logo {File}", safeName);
            return null;
        }
    }

    public void Delete(string fileName)
    {
        // Only a bare file name inside the branding folder: never a path supplied from outside.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeName))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(Folder, safeName));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete logo {File}", safeName);
        }
    }
}
