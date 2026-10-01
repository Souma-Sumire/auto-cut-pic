using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AutoCutPic.Core;
using AutoCutPic.Core.Abstractions;
using AutoCutPic.Core.Calculators;
using ImageMagick;

namespace AutoCutPic.ViewModels
{
    public enum AlignmentDirection
    {
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    public enum ViewMode
    {
        Single,
        Batch
    }

    public class PhotoViewModel : INotifyPropertyChanged
    {
        private double _offsetX;
        private double _offsetY;
        private BitmapSource? _thumbnail;
        private int _originalWidth;
        private int _originalHeight;

        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);

        public int OriginalWidth
        {
            get => _originalWidth;
            set
            {
                _originalWidth = value;
                OnPropertyChanged();
            }
        }

        public int OriginalHeight
        {
            get => _originalHeight;
            set
            {
                _originalHeight = value;
                OnPropertyChanged();
            }
        }

        public double OffsetX
        {
            get => _offsetX;
            set
            {
                _offsetX = Math.Clamp(value, -0.5, 0.5);
                OnPropertyChanged();
            }
        }

        public double OffsetY
        {
            get => _offsetY;
            set
            {
                _offsetY = Math.Clamp(value, -0.5, 0.5);
                OnPropertyChanged();
            }
        }

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        private CutMode _mode = CutMode.Fill;

        public CutMode Mode
        {
            get => _mode;
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isAspectMatched;
        public bool IsAspectMatched
        {
            get => _isAspectMatched;
            set
            {
                if (_isAspectMatched != value)
                {
                    _isAspectMatched = value;
                    OnPropertyChanged();
                }
            }
        }

        private TargetOrientation _orientation = TargetOrientation.Landscape;
        public TargetOrientation Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    OnPropertyChanged();
                }
            }
        }

        public PhotoViewModel(string path)
        {
            FilePath = path;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action execute)
            : this(_ => execute()) { }

        public RelayCommand(Action execute, Func<bool>? canExecute)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IImageProcessor _imageProcessor;

        private PhotoSize _selectedSize = PhotoSize.Inch6;
        private CutMode _selectedMode = CutMode.Fill;
        private string _statusText = "就绪";
        private double _zoomFactor = 1.0;
        private PhotoViewModel? _selectedPhoto;
        private bool _isExporting;
        private double _exportProgress;
        private CancellationTokenSource? _exportCts;

        public ObservableCollection<PhotoSize> Sizes { get; } =
            new()
            {
                PhotoSize.Inch3,
                PhotoSize.Inch5,
                PhotoSize.Inch6,
                PhotoSize.Inch7,
                PhotoSize.Inch8,
            };

        public ObservableCollection<PhotoViewModel> Photos { get; } = new();

        public PhotoSize SelectedSize
        {
            get => _selectedSize;
            set
            {
                if (_selectedSize != value)
                {
                    _selectedSize = value;
                    OnPropertyChanged();
                    UpdateAspectMatchForAll();
                    if (_matchedFilterMode != PhotoFilterMode.Show && SelectedPhoto?.IsAspectMatched == true)
                    {
                        var firstNeed = Photos.FirstOrDefault(p => !p.IsAspectMatched);
                        if (firstNeed != null)
                        {
                            SelectedPhoto = firstNeed;
                        }
                    }
                }
            }
        }

        public enum PhotoFilterMode
        {
            Show, // 全部显示
            Dim,  // 淡化免修 (默认)
            Hide  // 隐藏免修
        }

        private PhotoFilterMode _matchedFilterMode = PhotoFilterMode.Dim;
        public PhotoFilterMode MatchedFilterMode
        {
            get => _matchedFilterMode;
            set
            {
                if (_matchedFilterMode != value)
                {
                    _matchedFilterMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DimMatchedPhotos));
                    OnPropertyChanged(nameof(HideMatchedPhotos));
                    OnPropertyChanged(nameof(ShowAllPhotos));

                    if (_matchedFilterMode != PhotoFilterMode.Show && SelectedPhoto?.IsAspectMatched == true)
                    {
                        var firstNeed = Photos.FirstOrDefault(p => !p.IsAspectMatched);
                        if (firstNeed != null)
                        {
                            SelectedPhoto = firstNeed;
                        }
                    }
                }
            }
        }

        public bool DimMatchedPhotos
        {
            get => _matchedFilterMode == PhotoFilterMode.Dim;
            set
            {
                if (value) MatchedFilterMode = PhotoFilterMode.Dim;
            }
        }

        public bool HideMatchedPhotos
        {
            get => _matchedFilterMode == PhotoFilterMode.Hide;
            set
            {
                if (value) MatchedFilterMode = PhotoFilterMode.Hide;
            }
        }

        public bool ShowAllPhotos
        {
            get => _matchedFilterMode == PhotoFilterMode.Show;
            set
            {
                if (value) MatchedFilterMode = PhotoFilterMode.Show;
            }
        }

        public int AspectMatchedCount => Photos.Count(p => p.IsAspectMatched);
        public int NeedAdjustCount => Photos.Count(p => !p.IsAspectMatched);

        public void UpdateAspectMatchForAll()
        {
            if (SelectedSize == null) return;
            foreach (var photo in Photos)
            {
                photo.IsAspectMatched = AutoCutPic.Core.Calculators.CropGeometryCalculator.IsAspectMatched(
                    photo.OriginalWidth,
                    photo.OriginalHeight,
                    SelectedSize,
                    photo.Orientation);
            }
            OnPropertyChanged(nameof(AspectMatchedCount));
            OnPropertyChanged(nameof(NeedAdjustCount));
        }

        public CutMode SelectedMode
        {
            get => _selectedPhoto?.Mode ?? _selectedMode;
            set
            {
                _selectedMode = value;
                if (_selectedPhoto != null)
                {
                    _selectedPhoto.Mode = value;
                }
                OnPropertyChanged();
            }
        }

        private BitmapSource? _highResPreview;
        private CancellationTokenSource? _highResCts;

        public BitmapSource? HighResPreview
        {
            get => _highResPreview;
            set
            {
                _highResPreview = value;
                OnPropertyChanged();
            }
        }

        public PhotoViewModel? SelectedPhoto
        {
            get => _selectedPhoto;
            set
            {
                if (_selectedPhoto != value)
                {
                    _selectedPhoto = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedMode));
                    OnSelectedPhotoChanged(value);
                }
            }
        }

        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                _isExporting = value;
                OnPropertyChanged();
            }
        }

        public double ExportProgress
        {
            get => _exportProgress;
            set
            {
                _exportProgress = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoading;
        private double _loadingProgress;
        private string _loadingStatusText = "";

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDropOverlayVisible));
                }
            }
        }

        public double LoadingProgress
        {
            get => _loadingProgress;
            set
            {
                _loadingProgress = value;
                OnPropertyChanged();
            }
        }

        public string LoadingStatusText
        {
            get => _loadingStatusText;
            set
            {
                _loadingStatusText = value;
                OnPropertyChanged();
            }
        }

        private double _galleryCardWidth = 140.0;
        public double GalleryCardWidth
        {
            get => _galleryCardWidth;
            set
            {
                double clamped = Math.Clamp(Math.Round(value), 80.0, 300.0);
                if (Math.Abs(_galleryCardWidth - clamped) > 0.1)
                {
                    _galleryCardWidth = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GalleryCardHeight));
                    OnPropertyChanged(nameof(GalleryImageContainerHeight));
                    OnPropertyChanged(nameof(GalleryCardBoxWidth));
                    OnPropertyChanged(nameof(GalleryCardBoxHeight));
                }
            }
        }

        public double GalleryCardHeight => _galleryCardWidth + 40.0;
        public double GalleryImageContainerHeight => Math.Max(40.0, _galleryCardWidth - 44.0);
        public double GalleryCardBoxWidth => Math.Max(30.0, _galleryCardWidth - 12.0);
        public double GalleryCardBoxHeight => Math.Max(30.0, GalleryImageContainerHeight - 8.0);

        private ViewMode _currentViewMode = ViewMode.Single;

        public ViewMode CurrentViewMode
        {
            get => _currentViewMode;
            set
            {
                if (_currentViewMode != value)
                {
                    _currentViewMode = value;
                    OnPropertyChanged();
                }
            }
        }

        private void OnSelectedPhotoChanged(PhotoViewModel? photo)
        {
            _highResCts?.Cancel();
            if (photo == null)
            {
                HighResPreview = null;
                return;
            }

            HighResPreview = photo.Thumbnail;

            _highResCts = new CancellationTokenSource();
            var token = _highResCts.Token;

            Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    using var img = _imageProcessor.LoadImage(photo.FilePath);
                    if (token.IsCancellationRequested) return;

                    if (img.Width > 1600 || img.Height > 1600)
                    {
                        img.Resize(new MagickGeometry(1600, 1600));
                    }
                    var bs = ConvertToBitmapSource(img);
                    if (token.IsCancellationRequested) return;

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.Invoke(() =>
                        {
                            if (!token.IsCancellationRequested && SelectedPhoto == photo)
                            {
                                HighResPreview = bs;
                            }
                        });
                    }
                    else
                    {
                        if (!token.IsCancellationRequested && SelectedPhoto == photo)
                        {
                            HighResPreview = bs;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 高清预览加载失败: {photo.FilePath}, 原因: {ex.Message}");
                }
            }, token);
        }

        public double ZoomFactor
        {
            get => _zoomFactor;
            set
            {
                _zoomFactor = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public ICommand ExportCommand { get; }
        public ICommand CancelExportCommand { get; }
        public ICommand SwitchViewModeCommand { get; }
        public ICommand ToggleModeCommand { get; }
        public ICommand SetModeCommand { get; }
        public ICommand ToggleOrientationCommand { get; }
        public ICommand OpenFileInExplorerCommand { get; }
        public ICommand CopyFilePathCommand { get; }

        public void ToggleViewMode() =>
            CurrentViewMode = CurrentViewMode == ViewMode.Single ? ViewMode.Batch : ViewMode.Single;

        public void ToggleOrientation(System.Collections.IList? targetPhotos = null)
        {
            var list = targetPhotos?.OfType<PhotoViewModel>().ToList();
            if (list == null || list.Count == 0)
            {
                if (SelectedPhoto != null)
                    list = new List<PhotoViewModel> { SelectedPhoto };
            }

            if (list == null || list.Count == 0)
                return;

            foreach (var photo in list)
            {
                photo.Orientation = photo.Orientation == TargetOrientation.Landscape
                    ? TargetOrientation.Portrait
                    : TargetOrientation.Landscape;

                photo.IsAspectMatched = AutoCutPic.Core.Calculators.CropGeometryCalculator.IsAspectMatched(
                    photo.OriginalWidth,
                    photo.OriginalHeight,
                    SelectedSize,
                    photo.Orientation);
            }

            OnPropertyChanged(nameof(AspectMatchedCount));
            OnPropertyChanged(nameof(NeedAdjustCount));
        }

        public MainViewModel(IImageProcessor? imageProcessor = null)
        {
            _imageProcessor = imageProcessor ?? ImageProcessor.Instance;
            _selectedSize = Sizes.FirstOrDefault(s => s.Name == "6寸") ?? PhotoSize.Inch6;

            ToggleOrientationCommand = new RelayCommand(p =>
            {
                if (p is System.Collections.IList list)
                    ToggleOrientation(list);
                else
                    ToggleOrientation();
            });

            OpenFileInExplorerCommand = new RelayCommand(p =>
            {
                string? path = (p as PhotoViewModel)?.FilePath ?? SelectedPhoto?.FilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    catch { }
                }
            }, p => !string.IsNullOrEmpty((p as PhotoViewModel)?.FilePath ?? SelectedPhoto?.FilePath));

            CopyFilePathCommand = new RelayCommand(p =>
            {
                string? path = (p as PhotoViewModel)?.FilePath ?? SelectedPhoto?.FilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(path);
                    }
                    catch { }
                }
            }, p => !string.IsNullOrEmpty((p as PhotoViewModel)?.FilePath ?? SelectedPhoto?.FilePath));

            ToggleModeCommand = new RelayCommand(_ =>
            {
                if (SelectedPhoto != null)
                {
                    SelectedMode = SelectedMode == CutMode.Fill ? CutMode.Fit : CutMode.Fill;
                }
            });

            SetModeCommand = new RelayCommand(p =>
            {
                if (p is CutMode cm)
                {
                    SelectedMode = cm;
                }
                else if (p is string s && Enum.TryParse<CutMode>(s, true, out var parsed))
                {
                    SelectedMode = parsed;
                }
            });

            SwitchViewModeCommand = new RelayCommand(p =>
            {
                if (p is ViewMode vm)
                {
                    CurrentViewMode = vm;
                }
                else if (p is string s && Enum.TryParse<ViewMode>(s, true, out var parsed))
                {
                    CurrentViewMode = parsed;
                }
            });

            ExportCommand = new RelayCommand(
                async _ => await ExecuteExport(),
                _ => !IsExporting && Photos.Count > 0
            );

            CancelExportCommand = new RelayCommand(
                _ => _exportCts?.Cancel(),
                _ => IsExporting
            );
        }

        private bool _isInitialLoading;
        public bool IsInitialLoading
        {
            get => _isInitialLoading;
            set
            {
                if (_isInitialLoading != value)
                {
                    _isInitialLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDropOverlayVisible));
                }
            }
        }

        public bool IsDropOverlayVisible => Photos.Count == 0 || IsInitialLoading;

        public async Task LoadFiles(string[] allFiles)
        {
            if (allFiles == null || allFiles.Length == 0) return;

            IsLoading = true;
            LoadingProgress = 0;
            LoadingStatusText = $"正在准备导入照片 (0/{allFiles.Length})...";
            Photos.Clear();
            OnPropertyChanged(nameof(IsDropOverlayVisible));

            var photoList = allFiles.Select(f => new PhotoViewModel(f)).ToList();
            foreach (var p in photoList)
            {
                Photos.Add(p);
            }
            SelectedPhoto = Photos.FirstOrDefault();

            int total = photoList.Count;
            int loaded = 0;

            await Task.Run(() =>
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(photoList, options, photo =>
                {
                    LoadThumbnail(photo);

                    int current = Interlocked.Increment(ref loaded);
                    double pct = (double)current / total * 100.0;
                    string name = Path.GetFileName(photo.FilePath);

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.InvokeAsync(() =>
                        {
                            LoadingProgress = pct;
                            LoadingStatusText = $"正在载入第 {current}/{total} 张 ({pct:F0}%): {name}";
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                    }
                    else
                    {
                        LoadingProgress = pct;
                        LoadingStatusText = $"正在载入第 {current}/{total} 张 ({pct:F0}%): {name}";
                    }
                });
            });

            // 保持 100% 完成态短暂呈现，给用户清晰的视觉闭环
            LoadingProgress = 100.0;
            LoadingStatusText = $"已完成全部 {total} 张照片载入";
            await Task.Delay(200);

            IsLoading = false;
            IsInitialLoading = false;
            OnPropertyChanged(nameof(IsDropOverlayVisible));
            UpdateAspectMatchForAll();
            StatusText = $"已导入 {Photos.Count} 张照片";

            // 在淡化或隐藏免修模式下，优先选中第一张需要构图干预的非淡化照片
            if (MatchedFilterMode != PhotoFilterMode.Show && (SelectedPhoto == null || SelectedPhoto.IsAspectMatched))
            {
                var firstNeed = Photos.FirstOrDefault(p => !p.IsAspectMatched);
                if (firstNeed != null)
                {
                    SelectedPhoto = firstNeed;
                }
            }

            if (SelectedPhoto == null && Photos.Any())
            {
                SelectedPhoto = Photos[0];
            }
        }

        private void LoadThumbnail(PhotoViewModel photo)
        {
            try
            {
                using var image = _imageProcessor.LoadImage(photo.FilePath);
                int origW = (int)image.Width;
                int origH = (int)image.Height;

                // 检测自动剪裁方向：剔除靠近长边边缘的纯黑和纯白矩形条（自动识别手机相册截屏）
                var detectedOrientation = DetectEffectiveOrientation(image, origW, origH);

                image.Resize(new MagickGeometry(300, 300));
                var thumb = ConvertToBitmapSource(image);

                photo.Orientation = detectedOrientation;
                photo.OriginalWidth = origW;
                photo.OriginalHeight = origH;
                photo.Thumbnail = thumb;
                photo.IsAspectMatched = AutoCutPic.Core.Calculators.CropGeometryCalculator.IsAspectMatched(
                    origW, origH, _selectedSize, detectedOrientation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 缩略图生成失败: {photo.FilePath}, 原因: {ex.Message}");
            }
        }

        private static TargetOrientation DetectEffectiveOrientation(MagickImage image, int origW, int origH)
        {
            try
            {
                using var sample = (MagickImage)image.Clone();
                if (sample.Width > 160 || sample.Height > 160)
                {
                    sample.Resize(new MagickGeometry(160, 160));
                }
                var bytes = sample.ToByteArray(MagickFormat.Rgb);
                return AutoCutPic.Core.Calculators.CropGeometryCalculator.DetectEffectiveOrientation(bytes, (int)sample.Width, (int)sample.Height, 3);
            }
            catch
            {
                return origH > origW ? TargetOrientation.Portrait : TargetOrientation.Landscape;
            }
        }

        public void AdjustOffsetBatch(IEnumerable items, double dx, double dy)
        {
            foreach (PhotoViewModel photo in items)
            {
                photo.OffsetX = Math.Clamp(photo.OffsetX + dx, -0.5, 0.5);
                photo.OffsetY = Math.Clamp(photo.OffsetY + dy, -0.5, 0.5);
            }
        }

        public void AlignBatch(IEnumerable items, AlignmentDirection direction)
        {
            (double? ox, double? oy) target = direction switch
            {
                AlignmentDirection.Top => (null, -0.5),
                AlignmentDirection.Bottom => (null, 0.5),
                AlignmentDirection.Left => (-0.5, null),
                AlignmentDirection.Right => (0.5, null),
                AlignmentDirection.Center => (0.0, 0.0),
                _ => (null, null)
            };

            foreach (PhotoViewModel photo in items)
            {
                if (target.ox is double x) photo.OffsetX = x;
                if (target.oy is double y) photo.OffsetY = y;
            }
        }

        public void SetModeBatch(IEnumerable items, CutMode mode)
        {
            foreach (PhotoViewModel photo in items)
            {
                photo.Mode = mode;
            }
            _selectedMode = mode;
            OnPropertyChanged(nameof(SelectedMode));
        }

        private async Task ExecuteExport()
        {
            if (!Photos.Any())
            {
                StatusText = "请先添加图片";
                return;
            }

            string firstFileDir = Path.GetDirectoryName(Photos[0].FilePath) ?? "";
            string outputFolder = Path.Combine(firstFileDir, "Clipped_Photos");

            var exportItems = Photos
                .Select(p => new PhotoExportItem(p.FilePath, p.OffsetX, p.OffsetY, p.Mode, p.Orientation))
                .ToList();

            var cropSettings = new CropSettings
            {
                TargetSize = SelectedSize,
                Mode = SelectedMode
            };

            IsExporting = true;
            ExportProgress = 0;
            _exportCts = new CancellationTokenSource();

            var progress = new Progress<ExportProgressReport>(report =>
            {
                ExportProgress = report.Percentage;
                StatusText = $"正在导出 ({report.ProcessedCount}/{report.TotalCount}, {report.Percentage:F0}%): {report.CurrentFileName}";
            });

            try
            {
                var result = await _imageProcessor.ExportBatchAsync(
                    exportItems,
                    cropSettings,
                    outputFolder,
                    progress,
                    _exportCts.Token
                );

                if (result.IsCancelled)
                {
                    StatusText = $"导出已中止，已完成 {result.SuccessCount}/{result.TotalCount} 张。";
                }
                else if (result.FailedCount > 0)
                {
                    StatusText = $"导出完成：{result.SuccessCount} 成功，{result.FailedCount} 失败。";
                }
                else
                {
                    StatusText = $"导出完成！全部 {result.SuccessCount} 张照片已成功保存至 Clipped_Photos";
                }
            }
            finally
            {
                IsExporting = false;
                _exportCts.Dispose();
                _exportCts = null;
            }
        }

        private static BitmapSource ConvertToBitmapSource(MagickImage image)
        {
            using var ms = new MemoryStream();
            image.Write(ms, MagickFormat.Bmp);
            ms.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
