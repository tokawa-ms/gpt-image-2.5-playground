using GptImagePlayground.Core;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GptImagePlayground;

public sealed class ReferenceImporter
{
    private readonly string _sessionFolder = Path.Combine(App.LocalFolder, "imports", Guid.NewGuid().ToString("N"));

    public async Task<ReferenceImage> ImportAsync(StorageFile file)
    {
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size is 0 or >= (ulong)ImageRules.MaxReferenceBytes)
            throw new AppException(Text.Get("ReferenceTooLarge", file.Name));
        var extension = file.FileType.ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg")
            throw new AppException(Text.Get("ReferencePngJpegOnly", file.Name));
        Directory.CreateDirectory(_sessionFolder);
        var id = Guid.NewGuid().ToString("N");
        var copiedPath = Path.Combine(_sessionFolder, id + extension);
        var thumbnailPath = Path.Combine(_sessionFolder, id + "-thumb.png");
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_sessionFolder);
            var copied = await file.CopyAsync(folder, id + extension, NameCollisionOption.FailIfExists);
            var mediaType = await ImageRules.ValidateReferenceAsync(copied.Path);
            using var input = await copied.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            // Reject decompression bombs before any full-size bitmap is allocated.
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 32768 ||
                decoder.PixelHeight > 32768 || (long)decoder.PixelWidth * decoder.PixelHeight > 100_000_000)
                throw new AppException(Text.Get("ReferenceDimensionsExceeded", file.Name));
            var scale = Math.Min(1.0, 256.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)),
                ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var thumbnailFile = await folder.CreateFileAsync(id + "-thumb.png", CreationCollisionOption.FailIfExists);
            using var output = await thumbnailFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            return new ReferenceImage(copiedPath, file.Name, mediaType, thumbnailPath);
        }
        catch
        {
            if (File.Exists(copiedPath)) File.Delete(copiedPath);
            if (File.Exists(thumbnailPath)) File.Delete(thumbnailPath);
            throw;
        }
    }
}
