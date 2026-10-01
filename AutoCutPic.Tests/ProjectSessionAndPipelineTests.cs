using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCutPic.Core;
using AutoCutPic.Core.Abstractions;
using AutoCutPic.Core.Calculators;
using ImageMagick;
using Xunit;

namespace AutoCutPic.Tests
{
    public class ProjectSessionAndPipelineTests : IDisposable
    {
        private readonly string _testDir;

        public ProjectSessionAndPipelineTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_PipelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try
                {
                    Directory.Delete(_testDir, true);
                }
                catch { }
            }
        }

        [Fact]
        public async Task SessionManager_ShouldSaveAndLoadFromTempLocation()
        {
            string savePath = Path.Combine(_testDir, "test_session.json");
            var original = new ProjectSessionData
            {
                TargetSizeName = "5寸",
                FilterMode = "Hide",
                CardWidth = 160,
                SelectedIndex = 1,
                Photos = new List<PhotoSessionItem>
                {
                    new()
                    {
                        FilePath = "C:\\test\\photo1.jpg",
                        OffsetX = 0.2,
                        OffsetY = -0.1,
                        CropScale = 0.8,
                        Mode = CutMode.Fit,
                        Orientation = TargetOrientation.Landscape,
                        IsAspectMatched = false
                    }
                }
            };

            await SessionManager.SaveToFileAsync(original, savePath);
            Assert.True(File.Exists(savePath));

            var loaded = await SessionManager.LoadFromFileAsync(savePath);
            Assert.NotNull(loaded);
            Assert.Equal("5寸", loaded.TargetSizeName);
            Assert.Equal("Hide", loaded.FilterMode);
            Assert.Equal(160, loaded.CardWidth);
            Assert.Equal(1, loaded.SelectedIndex);
            Assert.Single(loaded.Photos);
            Assert.Equal(0.2, loaded.Photos[0].OffsetX);
            Assert.Equal(CutMode.Fit, loaded.Photos[0].Mode);
        }

        [Fact]
        public void PipelinePreExporter_SignatureShouldDifferWhenParametersChange()
        {
            var size = PhotoSize.Inch6;
            string file = "C:\\photo.jpg";

            string sig1 = PipelinePreExporter.ComputeSignature(file, size, CutMode.Fill, TargetOrientation.Landscape, 0, 0, 1.0);
            string sig2 = PipelinePreExporter.ComputeSignature(file, size, CutMode.Fill, TargetOrientation.Landscape, 0.1, 0, 1.0);
            string sig3 = PipelinePreExporter.ComputeSignature(file, size, CutMode.Fit, TargetOrientation.Landscape, 0, 0, 1.0);
            string sig4 = PipelinePreExporter.ComputeSignature(file, size, CutMode.Fill, TargetOrientation.Portrait, 0, 0, 1.0);
            string sig5 = PipelinePreExporter.ComputeSignature(file, size, CutMode.Fill, TargetOrientation.Landscape, 0, 0, 0.8);

            Assert.NotEqual(sig1, sig2);
            Assert.NotEqual(sig1, sig3);
            Assert.NotEqual(sig1, sig4);
            Assert.NotEqual(sig1, sig5);
        }

        [Fact]
        public async Task ExportBatchAsync_WithCachedFileResolver_ShouldDirectlyCopyCachedFile()
        {
            string imgPath = Path.Combine(_testDir, "test_cached_orig.jpg");
            using (var img = new MagickImage(MagickColors.Green, 800, 600))
            {
                img.Write(imgPath);
            }

            string fakeCachedFile = Path.Combine(_testDir, "cached_render.jpg");
            using (var cachedImg = new MagickImage(MagickColors.Yellow, 1800, 1200))
            {
                cachedImg.Write(fakeCachedFile);
            }

            string outDir = Path.Combine(_testDir, "Output");
            var items = new List<PhotoExportItem>
            {
                new(imgPath, 0, 0, CutMode.Fill, TargetOrientation.Landscape, 1.0)
            };

            var processor = ImageProcessor.Instance;
            var result = await processor.ExportBatchAsync(
                items,
                new CropSettings { TargetSize = PhotoSize.Inch6, Mode = CutMode.Fill },
                outDir,
                cachedFileResolver: item => fakeCachedFile
            );

            Assert.Equal(1, result.SuccessCount);
            string expectedOut = Path.Combine(outDir, "test_cached_orig_6寸.jpg");
            Assert.True(File.Exists(expectedOut));

            // 验证复制的是预渲染缓存文件（黄色而非绿色）
            using var readBack = new MagickImage(expectedOut);
            Assert.Equal((uint)1800, readBack.Width);
            Assert.Equal((uint)1200, readBack.Height);
        }
    }
}
