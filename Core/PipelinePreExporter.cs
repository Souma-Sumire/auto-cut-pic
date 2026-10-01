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

                    // 寻找符合安全定稿条件的照片：
                    // 1. 在当前正在裁的照片之前 (index < currentIndex)；若未选中任何照片，则检查全量
                    // 2. 距离最后一次改动或离开已超过 5 秒（防改动回退）
                    // 3. 尚未完成该构图签名的预导出
                    PreExportItem? candidate = null;
                    string candidateSig = string.Empty;

                    DateTime now = DateTime.UtcNow;
                    const double settleThresholdSeconds = 5.0;

                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        if (currentIndex >= 0 && i >= currentIndex)
                        {
                            // 当前及以后的照片用户可能还在修或观察，暂给定稿安全期
                            continue;
                        }

                        if ((now - item.LastModifiedUtc).TotalSeconds < settleThresholdSeconds)
                        {
                            // 离开时间未满 5 秒，暂不预导
                            continue;
                        }

                        string sig = ComputeSignature(item);
                        if (!_completedSignatures.TryGetValue(item.FilePath, out var doneSig) || doneSig != sig)
                        {
                            candidate = item;
                            candidateSig = sig;
                            break;
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
