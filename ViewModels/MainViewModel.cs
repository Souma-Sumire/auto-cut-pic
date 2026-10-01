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
                _selectedSize = value;
                OnPropertyChanged();
            }
        }

        public CutMode SelectedMode
        {
            get => _selectedMode;
            set
            {
                _selectedMode = value;
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

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested && SelectedPhoto == photo)
                        {
                            HighResPreview = bs;
                        }
                    });
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

        public MainViewModel(IImageProcessor? imageProcessor = null)
        {
            _imageProcessor = imageProcessor ?? ImageProcessor.Instance;
            _selectedSize = Sizes.FirstOrDefault(s => s.Name == "6寸") ?? PhotoSize.Inch6;

            ExportCommand = new RelayCommand(
                async _ => await ExecuteExport(),
                _ => !IsExporting && Photos.Count > 0
            );

            CancelExportCommand = new RelayCommand(
                _ => _exportCts?.Cancel(),
                _ => IsExporting
            );
        }

        public async Task LoadFiles(string[] allFiles)
        {
            Photos.Clear();
            StatusText = $"正在加载 {allFiles.Length} 张图片...";

            var loadTasks = allFiles
                .Select(file =>
                    Task.Run(() =>
                    {
                        var photo = new PhotoViewModel(file);
                        LoadThumbnail(photo);
                        return photo;
                    })
                )
                .ToList();

            var results = await Task.WhenAll(loadTasks);
            foreach (var photo in results)
            {
                Photos.Add(photo);
            }

            if (Photos.Any())
            {
                SelectedPhoto = Photos[0];
            }
            StatusText = $"已加载 {Photos.Count} 张图片";
        }

        private void LoadThumbnail(PhotoViewModel photo)
        {
            try
            {
                using var image = _imageProcessor.LoadImage(photo.FilePath);
                int origW = (int)image.Width;
                int origH = (int)image.Height;

                image.Resize(new MagickGeometry(300, 300));
                var thumb = ConvertToBitmapSource(image);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    photo.OriginalWidth = origW;
                    photo.OriginalHeight = origH;
                    photo.Thumbnail = thumb;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 缩略图生成失败: {photo.FilePath}, 原因: {ex.Message}");
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
            foreach (PhotoViewModel photo in items)
            {
                switch (direction)
                {
                    case AlignmentDirection.Top:
                        photo.OffsetY = -0.5;
                        break;
                    case AlignmentDirection.Bottom:
                        photo.OffsetY = 0.5;
                        break;
                    case AlignmentDirection.Left:
                        photo.OffsetX = -0.5;
                        break;
                    case AlignmentDirection.Right:
                        photo.OffsetX = 0.5;
                        break;
                    case AlignmentDirection.Center:
                        photo.OffsetX = 0.0;
                        photo.OffsetY = 0.0;
                        break;
                }
            }
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
                .Select(p => new PhotoExportItem(p.FilePath, p.OffsetX, p.OffsetY))
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
