using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoCutPic.Core.Abstractions;
using AutoCutPic.Core.Calculators;

namespace AutoCutPic.Core
{
    public record PreExportItem(
        string FilePath,
        double OffsetX,
        double OffsetY,
        double CropScale,
        CutMode Mode,
        TargetOrientation Orientation,
        PhotoSize TargetSize,
        DateTime LastModifiedUtc
    );

    public class PipelinePreExporter : IDisposable
    {
        private readonly IImageProcessor _imageProcessor;
        private readonly string _cacheDirectory;
        private readonly ConcurrentDictionary<string, string> _completedSignatures = new(); // FilePath -> Signature
        private readonly ConcurrentDictionary<string, string> _cachedFiles = new();        // Signature -> CachedFilePath

        private CancellationTokenSource? _loopCts;
        private Task? _workerTask;
        private bool _isDisposed;

        public event Action<int, int>? PreExportProgressChanged;

        public static string DefaultCacheDirectory =>
            Path.Combine(Path.GetTempPath(), "AutoCutPic", "PreExportCache");

        public PipelinePreExporter(IImageProcessor? imageProcessor = null, string? cacheDirectory = null)
        {
            _imageProcessor = imageProcessor ?? ImageProcessor.Instance;
            _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;
            Directory.CreateDirectory(_cacheDirectory);
        }

        public void Start(Func<IEnumerable<PreExportItem>> itemsProvider, Func<PreExportItem?> currentItemProvider, Func<bool> isExportingCheck)
        {
            Stop();

            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;

            _workerTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1200, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested) break;
                    if (isExportingCheck()) continue;

                    var items = itemsProvider()?.ToList();
                    if (items == null || items.Count == 0) continue;

                    var current = currentItemProvider();
                    int currentIndex = current != null ? items.FindIndex(i => i.FilePath == current.FilePath) : -1;

                    // 寻找符合安全定稿条件的照片（需静置离开或无修改超过 5 秒）：
                    // 1. 优先预导出在当前照片之前的照片 (i < currentIndex)
                    // 2. 若前面均已就绪，且用户在当前照片停留超过 5 秒未做修改，预导出当前照片 (i == currentIndex)
                    // 3. 若当前及之前均已就绪，预导出后续已静置满 5 秒的照片 (i > currentIndex)
                    PreExportItem? candidate = null;
                    string candidateSig = string.Empty;

                    DateTime now = DateTime.UtcNow;
                    const double settleThresholdSeconds = 5.0;

                    // 阶段 1：当前之前的已离开照片
                    int scanLimit = currentIndex >= 0 ? Math.Min(currentIndex, items.Count) : items.Count;
                    for (int i = 0; i < scanLimit; i++)
                    {
                        var item = items[i];
                        if ((now - item.LastModifiedUtc).TotalSeconds < settleThresholdSeconds) continue;

                        string sig = ComputeSignature(item);
                        if (!_completedSignatures.TryGetValue(item.FilePath, out var doneSig) || doneSig != sig)
                        {
                            candidate = item;
                            candidateSig = sig;
                            break;
                        }
                    }

                    // 阶段 2：当前照片本身（要求停留在上面且 5 秒内未修改）
                    if (candidate == null && currentIndex >= 0 && currentIndex < items.Count)
                    {
                        var item = items[currentIndex];
                        if ((now - item.LastModifiedUtc).TotalSeconds >= settleThresholdSeconds)
                        {
                            string sig = ComputeSignature(item);
                            if (!_completedSignatures.TryGetValue(item.FilePath, out var doneSig) || doneSig != sig)
                            {
                                candidate = item;
                                candidateSig = sig;
                            }
                        }
                    }

                    // 阶段 3：当前之后的照片（若已翻回前面，后面的已静置照片也按序预导出）
                    if (candidate == null && currentIndex >= 0 && currentIndex + 1 < items.Count)
                    {
                        for (int i = currentIndex + 1; i < items.Count; i++)
                        {
                            var item = items[i];
                            if ((now - item.LastModifiedUtc).TotalSeconds < settleThresholdSeconds) continue;

                            string sig = ComputeSignature(item);
                            if (!_completedSignatures.TryGetValue(item.FilePath, out var doneSig) || doneSig != sig)
                            {
                                candidate = item;
                                candidateSig = sig;
                                break;
                            }
                        }
                    }

                    if (candidate != null && !string.IsNullOrEmpty(candidateSig))
                    {
                        await ProcessCandidateAsync(candidate, candidateSig, token);

                        // 统计并派发已预导出进度
                        int readyCount = items.Count(it =>
                        {
                            string s = ComputeSignature(it);
                            return _completedSignatures.TryGetValue(it.FilePath, out var ds) && ds == s;
                        });
                        PreExportProgressChanged?.Invoke(readyCount, items.Count);
                    }
                }
            }, token);
        }

        public void Stop()
        {
            _loopCts?.Cancel();
            try
            {
                _workerTask?.Wait(500);
            }
            catch { }
            _loopCts?.Dispose();
            _loopCts = null;
            _workerTask = null;
        }

        public bool TryGetCachedExport(string filePath, PhotoSize size, CutMode mode, TargetOrientation orientation, double ox, double oy, double scale, out string cachedPath)
        {
            cachedPath = string.Empty;
            string sig = ComputeSignature(filePath, size, mode, orientation, ox, oy, scale);

            if (_completedSignatures.TryGetValue(filePath, out var doneSig) && doneSig == sig)
            {
                if (_cachedFiles.TryGetValue(sig, out var path) && File.Exists(path))
                {
                    cachedPath = path;
                    return true;
                }
            }
            return false;
        }

        public void Invalidate(string filePath)
        {
            _completedSignatures.TryRemove(filePath, out _);
        }

        public void MarkCompleted(IEnumerable<PreExportItem> items)
        {
            foreach (var item in items)
            {
                string sig = ComputeSignature(item);
                _completedSignatures[item.FilePath] = sig;
            }
        }

        public void ClearCache()
        {
            _completedSignatures.Clear();
            _cachedFiles.Clear();
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    Directory.Delete(_cacheDirectory, true);
                    Directory.CreateDirectory(_cacheDirectory);
                }
            }
            catch { }
        }

        private async Task ProcessCandidateAsync(PreExportItem item, string signature, CancellationToken token)
        {
            string outPath = Path.Combine(_cacheDirectory, $"{signature}.jpg");

            if (File.Exists(outPath))
            {
                _cachedFiles[signature] = outPath;
                _completedSignatures[item.FilePath] = signature;
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var image = _imageProcessor.LoadImage(item.FilePath);
                    token.ThrowIfCancellationRequested();

                    var settings = new CropSettings
                    {
                        TargetSize = item.TargetSize,
                        Mode = item.Mode
                    };

                    using var processed = _imageProcessor.ProcessImage(
                        image,
                        settings,
                        item.OffsetX,
                        item.OffsetY,
                        item.Orientation,
                        item.CropScale
                    );

                    token.ThrowIfCancellationRequested();

                    string tmp = outPath + ".tmp";
                    processed.Write(tmp);
                    File.Move(tmp, outPath, true);
                }, token);

                _cachedFiles[signature] = outPath;
                _completedSignatures[item.FilePath] = signature;
            }
            catch (OperationCanceledException)
            {
                // 取消时不记录
            }
            catch
            {
                // 单张失败忽略
            }
        }

        public static string ComputeSignature(PreExportItem item)
        {
            return ComputeSignature(item.FilePath, item.TargetSize, item.Mode, item.Orientation, item.OffsetX, item.OffsetY, item.CropScale);
        }

        public static string ComputeSignature(string filePath, PhotoSize size, CutMode mode, TargetOrientation orientation, double ox, double oy, double scale)
        {
            string raw = $"{filePath}|{size.Name}|{size.PixelWidth}x{size.PixelHeight}|{mode}|{orientation}|{ox:F4}|{oy:F4}|{scale:F4}";
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).Substring(0, 16);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
