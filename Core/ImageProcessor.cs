using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ImageMagick;

namespace AutoCutPic.Core
{
    public static class ImageProcessor
    {
        public static MagickImage LoadImage(string path)
        {
            var extension = Path.GetExtension(path).ToLower();

            if (extension == ".zip")
            {
                using var archive = ZipFile.OpenRead(path);
                var jpgEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                );
                if (jpgEntry != null)
                {
                    using var stream = jpgEntry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    var zipImg = new MagickImage(ms);
                    zipImg.AutoOrient();
                    return zipImg;
                }
            }

            var image = new MagickImage(Path.GetFullPath(path));
            image.AutoOrient();
            return image;
        }

        public static MagickImage ProcessImage(
            MagickImage original,
            CropSettings settings,
            double offsetX = 0,
            double offsetY = 0
        )
        {
            var result = (MagickImage)original.Clone();

            bool originIsPortrait = original.Height > original.Width;
            int baseW = Math.Max(settings.TargetSize.PixelWidth, settings.TargetSize.PixelHeight);
            int baseH = Math.Min(settings.TargetSize.PixelWidth, settings.TargetSize.PixelHeight);

            int targetW = originIsPortrait ? baseH : baseW;
            int targetH = originIsPortrait ? baseW : baseH;

            if (settings.Mode == CutMode.Fill)
            {
                double targetAR = (double)targetW / targetH;
                double photoAR = (double)original.Width / original.Height;

                int cropX, cropY, cropW, cropH;

                if (photoAR > targetAR)
                {
                    cropH = (int)original.Height;
                    cropW = (int)Math.Round(cropH * targetAR);
                    int excessW = (int)original.Width - cropW;
                    cropX = (int)Math.Round((excessW / 2.0) + (offsetX * excessW));
                    cropY = 0;
                }
                else
                {
                    cropW = (int)original.Width;
                    cropH = (int)Math.Round(cropW / targetAR);
                    int excessH = (int)original.Height - cropH;
                    cropX = 0;
                    cropY = (int)Math.Round((excessH / 2.0) + (offsetY * excessH));
                }

                cropX = Math.Max(0, Math.Min(cropX, (int)original.Width - cropW));
                cropY = Math.Max(0, Math.Min(cropY, (int)original.Height - cropH));

                result.Crop(new MagickGeometry(cropX, cropY, (uint)cropW, (uint)cropH));
                result.Resize(new MagickGeometry((uint)targetW, (uint)targetH)
                {
                    IgnoreAspectRatio = true
                });
            }
            else
            {
                double ratioW = (double)targetW / original.Width;
                double ratioH = (double)targetH / original.Height;
                double scale = Math.Min(ratioW, ratioH);

                result.Resize(
                    new MagickGeometry(
                        (uint)Math.Max(1, Math.Round(original.Width * scale)),
                        (uint)Math.Max(1, Math.Round(original.Height * scale))
                    )
                );

                result.BackgroundColor = MagickColors.White;
                result.Extent((uint)targetW, (uint)targetH, Gravity.Center);
            }

            result.Density = new Density(settings.TargetSize.Dpi, DensityUnit.PixelsPerInch);
            result.Settings.SetDefine(MagickFormat.Jpg, "quality", "95");

            return result;
        }
    }
}
