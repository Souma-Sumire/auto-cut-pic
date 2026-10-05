using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoCutPic.Core;
using AutoCutPic.Core.Calculators;
using ImageMagick;
using Xunit;

namespace AutoCutPic.Tests
{
    /// <summary>
    /// 预导出流水线契约：后台循环必须能挺过瞬时异常，缓存产物必须是真正的 JPEG。
    /// </summary>
    public class PreExportPipelineTests : IDisposable
    {
        private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(120);

        private readonly string _testDir;

        public PreExportPipelineTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_PreExportTests_" + Guid.NewGuid().ToString("N"));
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
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Test] 临时测试目录删除失败: {ex.Message}");
                }
            }
        }

        private string CreateSource(string fileName, MagickFormat format)
        {
            string path = Path.Combine(_testDir, fileName);
            using var image = new MagickImage(MagickColors.SeaGreen, 900, 600);
            image.Write(path, format);
            return path;
        }

        private static PreExportItem Settled(string path) => new(
            path, 0, 0, 1.0, CutMode.Fill, TargetOrientation.Landscape, PhotoSize.Inch6,
            DateTime.UtcNow.AddSeconds(-30));

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(40);
            }
        }

        [Fact]
        public async Task PreExporter_WhenProviderThrowsOnce_ShouldKeepPollingInsteadOfDyingForever()
        {
            string src = CreateSource("resilience.jpg", MagickFormat.Jpeg);
            var item = Settled(src);
            string cacheDir = Path.Combine(_testDir, "cache_resilience");

            int calls = 0;
            int ready = -1;
            var exporter = new PipelinePreExporter(ImageProcessor.Instance, cacheDir, FastPoll);
            exporter.PreExportProgressChanged += (r, _) => ready = r;

            // 第 2 次轮询抛异常：模拟 UI 线程正在批量导入时后台枚举照片集合失败
            exporter.Start(
                () =>
                {
                    if (Interlocked.Increment(ref calls) == 2)
                    {
                        throw new InvalidOperationException("集合已被修改（模拟并发导入）");
                    }
                    return new[] { item };
                },
                () => item,
                () => false);

            await WaitUntilAsync(() => Volatile.Read(ref calls) >= 6);
            exporter.Dispose();

            Assert.True(calls >= 6, $"一次异常就让预导出循环永久停摆，仅被调用 {calls} 次");
            Assert.Equal(1, ready);
        }

        [Fact]
        public async Task PreExporter_ShouldPreRenderSettledPhotoAndReportProgress()
        {
            string src = CreateSource("settled.jpg", MagickFormat.Jpeg);
            var item = Settled(src);
            string cacheDir = Path.Combine(_testDir, "cache_settled");

            int ready = -1;
            int total = -1;
            var exporter = new PipelinePreExporter(ImageProcessor.Instance, cacheDir, FastPoll);
            exporter.PreExportProgressChanged += (r, t) => { ready = r; total = t; };

            exporter.Start(() => new[] { item }, () => item, () => false);
            await WaitUntilAsync(() => ready >= 0);
            exporter.Dispose();

            Assert.Equal(1, ready);
            Assert.Equal(1, total);
            Assert.Single(Directory.GetFiles(cacheDir, "*.jpg"));
        }

        [Fact]
        public async Task PreExporter_WhenNotSettled_ShouldNotPreRenderYet()
        {
            string src = CreateSource("fresh.jpg", MagickFormat.Jpeg);
            // 刚被编辑过（未静置满 5 秒）的照片不得提前定稿
            var fresh = new PreExportItem(
                src, 0, 0, 1.0, CutMode.Fill, TargetOrientation.Landscape, PhotoSize.Inch6, DateTime.UtcNow);
            string cacheDir = Path.Combine(_testDir, "cache_fresh");

            int ready = -1;
            var exporter = new PipelinePreExporter(ImageProcessor.Instance, cacheDir, FastPoll);
            exporter.PreExportProgressChanged += (r, _) => ready = r;

            exporter.Start(() => new[] { fresh }, () => fresh, () => false);
            await Task.Delay(600);
            exporter.Dispose();

            Assert.Equal(-1, ready);
            Assert.Empty(Directory.GetFiles(cacheDir, "*.jpg"));
        }

        [Theory]
        [InlineData(MagickFormat.Png, "png")]
        [InlineData(MagickFormat.Bmp, "bmp")]
        [InlineData(MagickFormat.Tiff, "tif")]
        [InlineData(MagickFormat.Jpeg, "jpg")]
        public async Task PreExporter_CacheFile_ShouldAlwaysBeJpegContainer(MagickFormat sourceFormat, string extension)
        {
            // 缓存临时文件以 .tmp 结尾时 Magick 无法从扩展名推断格式、会退回源图格式，
            // 导出阶段 File.Copy 出来的 *_6寸.jpg 就会装着 PNG/BMP/TIFF 数据。
            string src = CreateSource($"src.{extension}", sourceFormat);
            var item = Settled(src);
            string cacheDir = Path.Combine(_testDir, "cache_fmt_" + extension);

            int ready = -1;
            var exporter = new PipelinePreExporter(ImageProcessor.Instance, cacheDir, FastPoll);
            exporter.PreExportProgressChanged += (r, _) => ready = r;
            exporter.Start(() => new[] { item }, () => item, () => false);

            await WaitUntilAsync(() => ready >= 0);
            exporter.Dispose();

            string[] cached = Directory.GetFiles(cacheDir, "*.jpg");
            Assert.Single(cached);

            using var cachedImage = new MagickImage(cached[0]);
            Assert.Equal(MagickFormat.Jpeg, cachedImage.Format);

            // 同时确认预渲染尺寸已符合目标相纸（6 寸横向 1795 x 1205）
            Assert.Equal(1795u, cachedImage.Width);
            Assert.Equal(1205u, cachedImage.Height);
        }

        [Fact]
        public void PruneCache_ShouldDropExpiredFilesAndStaleTempFiles()
        {
            string cacheDir = Path.Combine(_testDir, "cache_prune_age");
            Directory.CreateDirectory(cacheDir);

            string fresh = Path.Combine(cacheDir, "fresh.jpg");
            string expired = Path.Combine(cacheDir, "expired.jpg");
            string leftover = Path.Combine(cacheDir, "interrupted.jpg.tmp");
            File.WriteAllBytes(fresh, new byte[1024]);
            File.WriteAllBytes(expired, new byte[1024]);
            File.WriteAllBytes(leftover, new byte[16]);
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-30));

            using var exporter = new PipelinePreExporter(
                ImageProcessor.Instance, cacheDir, FastPoll, cacheRetention: TimeSpan.FromDays(7));

            Assert.True(File.Exists(fresh));
            Assert.False(File.Exists(expired));   // 超过保留时长
            Assert.False(File.Exists(leftover));  // 中断残留的临时文件
        }

        [Fact]
        public void PruneCache_WhenOverSizeLimit_ShouldDeleteOldestFirst()
        {
            string cacheDir = Path.Combine(_testDir, "cache_prune_size");
            Directory.CreateDirectory(cacheDir);

            // 4 个 1KB 文件，上限 2.5KB -> 必须按最旧优先删到 2 个
            for (int i = 0; i < 4; i++)
            {
                string path = Path.Combine(cacheDir, $"c{i}.jpg");
                File.WriteAllBytes(path, new byte[1024]);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10 + i));
            }

            using var exporter = new PipelinePreExporter(
                ImageProcessor.Instance, cacheDir, FastPoll,
                cacheRetention: TimeSpan.FromDays(3650), maxCacheBytes: 2560);

            string[] remaining = Directory.GetFiles(cacheDir, "*.jpg");
            Assert.Equal(2, remaining.Length);
            Assert.False(File.Exists(Path.Combine(cacheDir, "c0.jpg"))); // 最旧的两个被回收
            Assert.False(File.Exists(Path.Combine(cacheDir, "c1.jpg")));
            Assert.True(File.Exists(Path.Combine(cacheDir, "c2.jpg")));
            Assert.True(File.Exists(Path.Combine(cacheDir, "c3.jpg")));
        }

        [Fact]
        public void PruneCache_ShouldBeSafeOnMissingDirectory()
        {
            string cacheDir = Path.Combine(_testDir, "cache_missing", "nested");

            using var exporter = new PipelinePreExporter(ImageProcessor.Instance, cacheDir, FastPoll);

            Assert.True(Directory.Exists(cacheDir)); // 构造时创建，且清理不抛异常
            Assert.Empty(Directory.GetFiles(cacheDir));
        }
    }
}
