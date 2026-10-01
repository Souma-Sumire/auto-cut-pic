using System;
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
using ImageMagick;

namespace AutoCutPic.ViewModels
{
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

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private PhotoSize _selectedSize = PhotoSize.Inch6;
        private CutMode _selectedMode = CutMode.Fill;
        private string _statusText = "就绪";
        private double _zoomFactor = 1.0;
        private PhotoViewModel? _selectedPhoto;

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

        public PhotoViewModel? SelectedPhoto
        {
            get => _selectedPhoto;
            set
            {
                _selectedPhoto = value;
                OnPropertyChanged();
            }
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

        public MainViewModel()
        {
            ExportCommand = new RelayCommand(async () => await ExecuteExport());
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
                Photos.Add(photo);

            if (Photos.Any())
                SelectedPhoto = Photos[0];
            StatusText = $"已加载 {Photos.Count} 张图片";
        }

        private void LoadThumbnail(PhotoViewModel photo)
        {
            try
            {
                using var image = ImageProcessor.LoadImage(photo.FilePath);
                int origW = (int)image.Width;
                int origH = (int)image.Height;

                // 仅预览缩略图以节省资源
                image.Resize(new MagickGeometry(300, 300));
                var thumb = ConvertToBitmapSource(image);

                // 转回 UI 线程
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    photo.OriginalWidth = origW;
                    photo.OriginalHeight = origH;
                    photo.Thumbnail = thumb;
                });
            }
            catch { }
        }

        public void AdjustOffsetBatch(System.Collections.IEnumerable items, double dx, double dy)
        {
            foreach (PhotoViewModel photo in items)
            {
                photo.OffsetX = Math.Clamp(photo.OffsetX + dx, -0.5, 0.5);
                photo.OffsetY = Math.Clamp(photo.OffsetY + dy, -0.5, 0.5);
            }
        }

        public void AlignBatch(System.Collections.IEnumerable items, string direction)
        {
            foreach (PhotoViewModel photo in items)
            {
                switch (direction.ToLower())
                {
                    case "top":
                    case "w":
                        photo.OffsetY = -0.5;
                        break;
                    case "bottom":
                    case "s":
                        photo.OffsetY = 0.5;
                        break;
                    case "left":
                    case "a":
                        photo.OffsetX = -0.5;
                        break;
                    case "right":
                    case "d":
                        photo.OffsetX = 0.5;
                        break;
                }
            }
        }

        private async Task ExecuteExport()
        {
            if (!Photos.Any())
            {
                StatusText = "请先拖入图片";
                return;
            }
            string outputFolder = Path.Combine(
                Path.GetDirectoryName(Photos[0].FilePath) ?? "",
                "Clipped_Photos"
            );
            await ExportAll(Photos.ToList(), outputFolder);
        }

        private async Task ExportAll(List<PhotoViewModel> photos, string outputFolder)
        {
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);
            StatusText = "正在导出...";
            int count = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(
                    photos,
                    photo =>
                    {
                        try
                        {
                            using var image = ImageProcessor.LoadImage(photo.FilePath);
                            using var processed = ImageProcessor.ProcessImage(
                                image,
                                new CropSettings { TargetSize = SelectedSize, Mode = SelectedMode },
                                photo.OffsetX,
                                photo.OffsetY
                            );

                            string outPath = Path.Combine(
                                outputFolder,
                                $"{Path.GetFileNameWithoutExtension(photo.FilePath)}_{SelectedSize.Name}.jpg"
                            );
                            processed.Write(outPath);
                            Interlocked.Increment(ref count);
                        }
                        catch { }
                    }
                );
            });
            StatusText = $"导出完成！已处理 {count} 张。";
        }

        private BitmapSource ConvertToBitmapSource(MagickImage image)
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
