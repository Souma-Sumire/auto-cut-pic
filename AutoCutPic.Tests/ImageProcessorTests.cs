using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCutPic.Core;
using AutoCutPic.Core.Abstractions;
using ImageMagick;
using Xunit;

namespace AutoCutPic.Tests
{
    public class ImageProcessorTests : IDisposable
    {
        private readonly string _testDir;

        public ImageProcessorTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_Tests_" + Guid.NewGuid().ToString("N"));
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
        public async Task ExportBatchAsync_ShouldReportProgressAndExportSuccessfully()
        {
            // 准备两张测试原图
            string img1 = Path.Combine(_testDir, "test1.jpg");
            string img2 = Path.Combine(_testDir, "test2.jpg");

            using (var m1 = new MagickImage(MagickColors.Red, 800, 600))
            {
                m1.Write(img1);
            }
            using (var m2 = new MagickImage(MagickColors.Blue, 600, 800))
            {
                m2.Write(img2);
            }

            var processor = ImageProcessor.Instance;
            var items = new List<PhotoExportItem>
            {
                new(img1, 0, 0),
                new(img2, 0, 0)
            };

            var settings = new CropSettings
            {
                TargetSize = PhotoSize.Inch6,
                Mode = CutMode.Fill
            };

            string outDir = Path.Combine(_testDir, "Out");
            var reports = new List<ExportProgressReport>();
            var progress = new Progress<ExportProgressReport>(r => reports.Add(r));

            var result = await processor.ExportBatchAsync(items, settings, outDir, progress, CancellationToken.None);

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(0, result.FailedCount);
            Assert.False(result.IsCancelled);

            // 验证生成的文件是否存在且有效
            string out1 = Path.Combine(outDir, "test1_6寸.jpg");
            string out2 = Path.Combine(outDir, "test2_6寸.jpg");
            Assert.True(File.Exists(out1));
            Assert.True(File.Exists(out2));

            using var verifyImg1 = new MagickImage(out1);
            // 6寸横向: 1795 x 1205
            Assert.Equal(1795u, verifyImg1.Width);
            Assert.Equal(1205u, verifyImg1.Height);
            Assert.Equal(300, (int)verifyImg1.Density.X);

            using var verifyImg2 = new MagickImage(out2);
            // 6寸纵向自适应: 1205 x 1795
            Assert.Equal(1205u, verifyImg2.Width);
            Assert.Equal(1795u, verifyImg2.Height);
            Assert.Equal(300, (int)verifyImg2.Density.X);
        }

        [Fact]
        public async Task ExportBatchAsync_WhenCancelled_ShouldSetIsCancelled()
        {
            string img1 = Path.Combine(_testDir, "cancel_test.jpg");
            using (var m1 = new MagickImage(MagickColors.Green, 400, 300))
            {
                m1.Write(img1);
            }

            var processor = ImageProcessor.Instance;
            var items = new List<PhotoExportItem>
            {
                new(img1, 0, 0)
            };

            var settings = new CropSettings
            {
                TargetSize = PhotoSize.Inch6,
                Mode = CutMode.Fill
            };

            string outDir = Path.Combine(_testDir, "OutCancel");
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // 提前取消

            var result = await processor.ExportBatchAsync(items, settings, outDir, null, cts.Token);
            Assert.True(result.IsCancelled);
        }
    }
}
