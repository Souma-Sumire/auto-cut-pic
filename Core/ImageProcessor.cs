using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoCutPic.Core.Abstractions;
using AutoCutPic.Core.Calculators;
using ImageMagick;

namespace AutoCutPic.Core
{
    public class ImageProcessor : IImageProcessor
    {
        public static ImageProcessor Instance { get; } = new();

        public MagickImage LoadImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

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

        public MagickImage ProcessImage(
            MagickImage original,
            CropSettings settings,
            double offsetX = 0,
            double offsetY = 0,
            TargetOrientation? orientation = null,
            double cropScale = 1.0
        )
        {
            var result = (MagickImage)original.Clone();

            var targetPaper = CropGeometryCalculator.CalculateTargetPaperDimensions(
                settings.TargetSize,
                (int)original.Width,
                (int)original.Height,
                orientation
            );

            if (settings.Mode == CutMode.Fill)
            {
                var cropRect = CropGeometryCalculator.CalculateFillCrop(
                    (int)original.Width,
                    (int)original.Height,
                    targetPaper,
                    offsetX,
                    offsetY,
                    cropScale
                );

                result.Crop(new MagickGeometry(cropRect.X, cropRect.Y, (uint)cropRect.Width, (uint)cropRect.Height));
                result.Resize(new MagickGeometry((uint)targetPaper.Width, (uint)targetPaper.Height)
                {
                    IgnoreAspectRatio = true
                });
            }
            else
            {
                var placement = CropGeometryCalculator.CalculateFitPlacement(
                    (int)original.Width,
                    (int)original.Height,
                    targetPaper
                );

                result.Resize(
                    new MagickGeometry(
                        (uint)placement.ScaledWidth,
                        (uint)placement.ScaledHeight
                    )
                );

                result.BackgroundColor = MagickColors.White;
                result.Extent((uint)targetPaper.Width, (uint)targetPaper.Height, Gravity.Center);
            }

            result.Density = new Density(settings.TargetSize.Dpi, DensityUnit.PixelsPerInch);
            result.Settings.SetDefine(MagickFormat.Jpg, "quality", "95");

            return result;
        }

        public async Task<ExportBatchResult> ExportBatchAsync(
            IReadOnlyList<PhotoExportItem> items,
            CropSettings settings,
            string outputFolder,
            IProgress<ExportProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            Func<PhotoExportItem, string?>? cachedFileResolver = null
        )
        {
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            int processedCount = 0;
            int successCount = 0;
            var failures = new ConcurrentBag<ExportFailure>();
            bool isCancelled = false;

            await Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                try
                {
                    Parallel.ForEach(items, parallelOptions, item =>
                    {
                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(item.FilePath);
                            string outPath = Path.Combine(
                                outputFolder,
                                $"{fileName}_{settings.TargetSize.Name}.jpg"
                            );

                            string? cachedFile = cachedFileResolver?.Invoke(item);
                            if (!string.IsNullOrEmpty(cachedFile) && File.Exists(cachedFile))
                            {
                                File.Copy(cachedFile, outPath, true);
                                Interlocked.Increment(ref successCount);
                                return;
                            }

                            using var image = LoadImage(item.FilePath);
                            var effectiveMode = item.Mode ?? settings.Mode;
                            var itemSettings = effectiveMode == settings.Mode ? settings : settings with { Mode = effectiveMode };
                            using var processed = ProcessImage(
                                image,
                                itemSettings,
                                item.OffsetX,
                                item.OffsetY,
                                item.Orientation,
                                item.CropScale
                            );

                            processed.Write(outPath);
                            Interlocked.Increment(ref successCount);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            failures.Add(new ExportFailure(item.FilePath, ex.Message));
                        }
                        finally
                        {
                            int current = Interlocked.Increment(ref processedCount);
                            progress?.Report(new ExportProgressReport(
                                current,
                                items.Count,
                                Path.GetFileName(item.FilePath)
                            ));
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    isCancelled = true;
                }
            }, CancellationToken.None);

            return new ExportBatchResult
            {
                TotalCount = items.Count,
                SuccessCount = successCount,
                IsCancelled = isCancelled,
                Failures = failures.ToList()
            };
        }
    }
}
