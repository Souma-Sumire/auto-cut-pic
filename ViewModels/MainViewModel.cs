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

    /// <summary>相邻照片切换的结果，用于在到达边界时提示用户而不是绕回另一端。</summary>
    public enum PhotoNavigationStatus
    {
        /// <summary>没有照片可切换。</summary>
        Empty,

        /// <summary>已切换到目标照片。</summary>
        Moved,

        /// <summary>已经是第一张。</summary>
        ReachedFirst,

        /// <summary>已经是最后一张。</summary>
        ReachedLast,

        /// <summary>该方向剩余的均为免修照片，没有需要调整的照片。</summary>
        NoActionableInDirection
    }

    public class PhotoViewModel : INotifyPropertyChanged
    {
        private double _fillOffsetX;
        private double _fillOffsetY;
        private double _fitOffsetX;
        private double _fitOffsetY;
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

        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 当前裁切模式下的归一化构图偏移量（定义域 [-0.5, 0.5]）。
        /// <para>
        /// 裁剪填充（Fill）与留白完整（Fit）各自独立记忆偏移量：Fill 的偏移量表示
        /// 裁切窗口在原图内的取景位置，Fit 的偏移量表示照片在相纸白边内的排版位置，
        /// 两者归一化基准完全不同，因此切换模式时互不影响。
        /// </para>
        /// </summary>
        public double OffsetX
        {
            get => _mode == CutMode.Fit ? _fitOffsetX : _fillOffsetX;
            set
            {
                double clamped = Math.Clamp(value, -0.5, 0.5);
                if (_mode == CutMode.Fit)
                {
                    _fitOffsetX = clamped;
                }
                else
                {
                    _fillOffsetX = clamped;
                }

                LastModifiedUtc = DateTime.UtcNow;
                OnPropertyChanged();
            }
        }

        /// <summary>当前裁切模式下的归一化构图偏移量，语义同 <see cref="OffsetX"/>。</summary>
        public double OffsetY
        {
            get => _mode == CutMode.Fit ? _fitOffsetY : _fillOffsetY;
            set
            {
                double clamped = Math.Clamp(value, -0.5, 0.5);
                if (_mode == CutMode.Fit)
                {
                    _fitOffsetY = clamped;
                }
                else
                {
                    _fillOffsetY = clamped;
                }

                LastModifiedUtc = DateTime.UtcNow;
                OnPropertyChanged();
            }
        }

        /// <summary>裁剪填充（Fill）模式的偏移量，供工程序列化使用。</summary>
        public double FillOffsetX => _fillOffsetX;

        /// <summary>裁剪填充（Fill）模式的偏移量，供工程序列化使用。</summary>
        public double FillOffsetY => _fillOffsetY;

        /// <summary>留白完整（Fit）模式的偏移量，供工程序列化使用。</summary>
        public double FitOffsetX => _fitOffsetX;

        /// <summary>留白完整（Fit）模式的偏移量，供工程序列化使用。</summary>
        public double FitOffsetY => _fitOffsetY;

        /// <summary>
        /// 一次性写入两种模式各自的构图偏移量（用于工程/草稿恢复），
        /// 避免直接赋值时受 <see cref="Mode"/> 当前值影响。
        /// </summary>
        public void ApplyModeOffsets(double fillX, double fillY, double fitX, double fitY)
        {
            _fillOffsetX = Math.Clamp(fillX, -0.5, 0.5);
            _fillOffsetY = Math.Clamp(fillY, -0.5, 0.5);
            _fitOffsetX = Math.Clamp(fitX, -0.5, 0.5);
            _fitOffsetY = Math.Clamp(fitY, -0.5, 0.5);
            OnPropertyChanged(nameof(OffsetX));
            OnPropertyChanged(nameof(OffsetY));
        }

        /// <summary>同时清空两种模式的构图偏移量（R 键重置构图）。</summary>
        public void ResetModeOffsets()
        {
            ApplyModeOffsets(0, 0, 0, 0);
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
                    LastModifiedUtc = DateTime.UtcNow;
                    OnPropertyChanged();
                    // 两种模式各自持有偏移量，切换后必须通知界面重新读取当前模式的构图
                    OnPropertyChanged(nameof(OffsetX));
                    OnPropertyChanged(nameof(OffsetY));
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

        private double _cropScale = 1.0;
        public double CropScale
        {
            get => _cropScale;
            set
            {
                double clamped = Math.Clamp(value, 0.2, 1.0);
                if (Math.Abs(_cropScale - clamped) > 0.0001)
                {
                    _cropScale = clamped;
                    LastModifiedUtc = DateTime.UtcNow;
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
                    LastModifiedUtc = DateTime.UtcNow;
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

    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IImageProcessor _imageProcessor;
        private readonly PipelinePreExporter _preExporter;
        private readonly string _autoSaveFilePath;

        private PhotoSize _selectedSize = PhotoSize.Inch6;
        private CutMode _selectedMode = CutMode.Fill;
        private string _statusText = "就绪";
        private string _preExportStatusText = "";
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
                    MarkSessionDirty();
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

        public int CurrentPhotoIndex => _selectedPhoto != null && Photos.Count > 0
            ? Photos.IndexOf(_selectedPhoto) + 1
            : 0;

        public int TotalPhotosCount => Photos.Count;

        public double OverallProgressPercent
        {
            get => Photos.Count > 0 && CurrentPhotoIndex > 0
                ? Math.Round((double)CurrentPhotoIndex / Photos.Count * 100.0, 1)
                : 0.0;
            set { }
        }

        public string OverallProgressText => Photos.Count > 0 && CurrentPhotoIndex > 0
            ? $"{CurrentPhotoIndex} / {Photos.Count} ({OverallProgressPercent:F0}%)"
            : "0 / 0 (0%)";

        public bool HasPhotos => Photos.Count > 0;

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
                    OnPropertyChanged(nameof(CurrentPhotoIndex));
                    OnPropertyChanged(nameof(TotalPhotosCount));
                    OnPropertyChanged(nameof(OverallProgressPercent));
                    OnPropertyChanged(nameof(OverallProgressText));
                    OnPropertyChanged(nameof(HasPhotos));
                    OnSelectedPhotoChanged(value);
                    RequestAutoSave();
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

        private double _galleryCardWidth = 130.0;
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

        public double GalleryImageContainerHeight => Math.Round(_galleryCardWidth * 0.72);
        public double GalleryCardBoxWidth => Math.Max(20.0, _galleryCardWidth - 12.0);
        public double GalleryCardBoxHeight => Math.Max(20.0, GalleryImageContainerHeight - 4.0);
        public double GalleryCardHeight => GalleryImageContainerHeight + 38.0;

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

        private string _autoSaveStatusText = "自动保存就绪";
        public string AutoSaveStatusText
        {
            get => _autoSaveStatusText;
            set
            {
                _autoSaveStatusText = value;
                OnPropertyChanged();
            }
        }

        public string PreExportStatusText
        {
            get => _preExportStatusText;
            set
            {
                if (_preExportStatusText != value)
                {
                    _preExportStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _currentProjectPath;
        public string? CurrentProjectPath
        {
            get => _currentProjectPath;
            set
            {
                _currentProjectPath = value;
                OnPropertyChanged();
            }
        }

        public bool HasAutoSaveDraft => SessionManager.HasAutoSaveDraft(_autoSaveFilePath);

        /// <summary>当前工作区草稿的落盘路径（默认位于系统临时目录）。</summary>
        public string AutoSaveFilePath => _autoSaveFilePath;

        private bool _hasUnexportedChanges;

        /// <summary>
        /// 工作区是否存在尚未导出的编辑内容。
        /// 只有为 true 时才允许生成/保留草稿文件，全部导出成功后自动转为 false 并清理草稿。
        /// </summary>
        public bool HasUnexportedChanges => _hasUnexportedChanges;

        /// <summary>标记工作区产生了未导出的编辑内容。</summary>
        public void MarkSessionDirty()
        {
            if (_hasUnexportedChanges)
            {
                return;
            }

            _hasUnexportedChanges = true;
            OnPropertyChanged(nameof(HasUnexportedChanges));
        }

        private bool _hasRecoverableDraft;

        /// <summary>启动时是否探测到可恢复的历史工作区草稿（仅提示，绝不自动载入）。</summary>
        public bool HasRecoverableDraft
        {
            get => _hasRecoverableDraft;
            private set
            {
                if (_hasRecoverableDraft != value)
                {
                    _hasRecoverableDraft = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _recoverableDraftText = "";

        /// <summary>可恢复草稿的摘要文案（供空状态卡片展示）。</summary>
        public string RecoverableDraftText
        {
            get => _recoverableDraftText;
            private set
            {
                if (_recoverableDraftText != value)
                {
                    _recoverableDraftText = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand RestoreDraftCommand { get; }
        public ICommand DiscardDraftCommand { get; }

        private Timer? _autoSaveTimer;
        private readonly object _autoSaveLock = new();

        public void RequestAutoSave()
        {
            if (Photos.Count == 0 || !_hasUnexportedChanges) return;

            lock (_autoSaveLock)
            {
                _autoSaveTimer?.Dispose();
                _autoSaveTimer = new Timer(async _ =>
                {
                    await SaveAutoSaveSessionAsync();
                }, null, 4000, Timeout.Infinite);
            }
        }

        public async Task SaveAutoSaveSessionAsync()
        {
            if (Photos.Count == 0) return;

            // 已全部导出的工作区不再产生草稿，避免下次启动被误判为"未完成的工作"。
            if (!_hasUnexportedChanges)
            {
                SessionManager.ClearAutoSaveDraft(_autoSaveFilePath);
                return;
            }

            try
            {
                var data = CreateSessionSnapshot();
                await SessionManager.SaveToFileAsync(data, _autoSaveFilePath);
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                    {
                        AutoSaveStatusText = $"已自动保存 {DateTime.Now:HH:mm:ss}";
                        OnPropertyChanged(nameof(HasAutoSaveDraft));
                    });
                }
                else
                {
                    AutoSaveStatusText = $"已自动保存 {DateTime.Now:HH:mm:ss}";
                    OnPropertyChanged(nameof(HasAutoSaveDraft));
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 自动保存草稿失败: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 自动保存草稿被拒绝: {ex.Message}");
            }
        }

        public void SaveSessionImmediately()
        {
            if (Photos.Count == 0) return;

            if (!_hasUnexportedChanges)
            {
                SessionManager.ClearAutoSaveDraft(_autoSaveFilePath);
                return;
            }

            try
            {
                var data = CreateSessionSnapshot();
                SessionManager.SaveToFile(data, _autoSaveFilePath);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 关闭时保存草稿失败: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 关闭时保存草稿被拒绝: {ex.Message}");
            }
        }

        /// <summary>
        /// 工作区已被完整导出：停止自动保存计时器、清理草稿文件，
        /// 并在用户产生新的编辑之前不再写入草稿。
        /// </summary>
        private void MarkSessionExported()
        {
            _hasUnexportedChanges = false;
            OnPropertyChanged(nameof(HasUnexportedChanges));

            lock (_autoSaveLock)
            {
                _autoSaveTimer?.Dispose();
                _autoSaveTimer = null;
            }

            SessionManager.ClearAutoSaveDraft(_autoSaveFilePath);
            ApplyRecoverableDraft(null, "");
            OnPropertyChanged(nameof(HasAutoSaveDraft));
            AutoSaveStatusText = "已全部导出，工作区草稿已归档";
        }

        /// <summary>
        /// 仅探测草稿元信息并更新提示状态，<b>不会</b>载入任何照片。
        /// 程序启动时调用，用于向用户提供显式的恢复入口。
        /// </summary>
        public async Task<bool> RefreshRecoverableDraftAsync()
        {
            ProjectSessionData? draft = null;
            try
            {
                draft = await SessionManager.LoadFromFileAsync(_autoSaveFilePath);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 读取工作区草稿失败: {ex.Message}");
            }

            bool usable = draft?.Photos != null && draft.Photos.Any(p => File.Exists(p.FilePath));
            ApplyRecoverableDraft(usable ? draft : null, usable ? BuildDraftSummary(draft) : "");
            return usable;
        }

        /// <summary>用户显式确认后恢复历史工作区草稿。</summary>
        public async Task<bool> RestoreAutoSaveDraftAsync()
        {
            ProjectSessionData? draft = null;
            try
            {
                draft = await SessionManager.LoadFromFileAsync(_autoSaveFilePath);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 读取工作区草稿失败: {ex.Message}");
            }

            if (draft == null)
            {
                ApplyRecoverableDraft(null, "");
                return false;
            }

            bool restored = await RestoreSessionAsync(draft);
            if (!restored)
            {
                ApplyRecoverableDraft(null, "");
                return false;
            }

            ApplyRecoverableDraft(null, "");
            return true;
        }

        /// <summary>用户显式丢弃历史工作区草稿。</summary>
        public void DiscardAutoSaveDraft()
        {
            SessionManager.ClearAutoSaveDraft(_autoSaveFilePath);
            ApplyRecoverableDraft(null, "");
            OnPropertyChanged(nameof(HasAutoSaveDraft));
            StatusText = "已丢弃上次工作区草稿";
        }

        private void ApplyRecoverableDraft(ProjectSessionData? draft, string summary)
        {
            HasRecoverableDraft = draft != null;
            RecoverableDraftText = draft != null ? summary : "";
        }

        private static string BuildDraftSummary(ProjectSessionData? draft)
        {
            if (draft?.Photos == null || draft.Photos.Count == 0)
            {
                return "";
            }

            int existing = draft.Photos.Count(p => File.Exists(p.FilePath));
            return $"{existing} 张照片 · 保存于 {draft.LastSavedTime.ToLocalTime():yyyy-MM-dd HH:mm}";
        }

        public ProjectSessionData CreateSessionSnapshot()
        {
            return new ProjectSessionData
            {
                LastSavedTime = DateTime.UtcNow,
                TargetSizeName = SelectedSize.Name,
                FilterMode = MatchedFilterMode.ToString(),
                CardWidth = GalleryCardWidth,
                SelectedIndex = SelectedPhoto != null ? Photos.IndexOf(SelectedPhoto) : 0,
                Photos = Photos.Select(p => new PhotoSessionItem
                {
                    FilePath = p.FilePath,
                    OffsetX = p.FillOffsetX,
                    OffsetY = p.FillOffsetY,
                    OffsetXFit = p.FitOffsetX,
                    OffsetYFit = p.FitOffsetY,
                    CropScale = p.CropScale,
                    Mode = p.Mode,
                    Orientation = p.Orientation,
                    IsAspectMatched = p.IsAspectMatched
                }).ToList()
            };
        }

        public async Task<bool> RestoreSessionAsync(ProjectSessionData data)
        {
            if (data == null || data.Photos == null || data.Photos.Count == 0) return false;

            var validItems = data.Photos.Where(p => File.Exists(p.FilePath)).ToList();
            if (validItems.Count == 0) return false;

            IsLoading = true;
            LoadingProgress = 0;
            LoadingStatusText = "正在恢复工作进度...";
            Photos.Clear();

            var matchedSize = Sizes.FirstOrDefault(s => s.Name == data.TargetSizeName);
            if (matchedSize != null) SelectedSize = matchedSize;
            if (Enum.TryParse<PhotoFilterMode>(data.FilterMode, out var fm)) MatchedFilterMode = fm;
            if (data.CardWidth >= 80 && data.CardWidth <= 300) GalleryCardWidth = data.CardWidth;

            var photoList = new List<PhotoViewModel>();
            foreach (var item in validItems)
            {
                var p = new PhotoViewModel(item.FilePath)
                {
                    CropScale = item.CropScale,
                    Mode = item.Mode,
                    Orientation = item.Orientation,
                    IsAspectMatched = item.IsAspectMatched
                };

                var offsets = item.ResolveOffsets();
                p.ApplyModeOffsets(offsets.FillX, offsets.FillY, offsets.FitX, offsets.FitY);

                AttachPhotoChangeHandler(p);
                photoList.Add(p);
            }

            int total = photoList.Count;
            int loaded = 0;
            await Task.Run(() =>
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
                Parallel.ForEach(photoList, options, photo =>
                {
                    LoadThumbnail(photo);
                    Interlocked.Increment(ref loaded);
                });
            });

            await StreamPopulatePhotosAsync(photoList);

            IsLoading = false;
            IsInitialLoading = false;
            OnPropertyChanged(nameof(IsDropOverlayVisible));
            OnPropertyChanged(nameof(HasPhotos));
            OnPropertyChanged(nameof(TotalPhotosCount));
            OnPropertyChanged(nameof(CurrentPhotoIndex));
            OnPropertyChanged(nameof(OverallProgressPercent));
            OnPropertyChanged(nameof(OverallProgressText));

            int targetIdx = Math.Clamp(data.SelectedIndex, 0, Photos.Count - 1);
            SelectedPhoto = Photos[targetIdx];

            MarkSessionDirty();
            StatusText = $"已恢复工作区进度 (共 {Photos.Count} 张照片)";
            AutoSaveStatusText = $"已恢复 {DateTime.Now:HH:mm:ss}";
            return true;
        }

        public void SaveProjectToFile(string filePath)
        {
            var data = CreateSessionSnapshot();
            SessionManager.SaveToFile(data, filePath);
            CurrentProjectPath = filePath;
            StatusText = $"已保存项目工程: {Path.GetFileName(filePath)}";
            AutoSaveStatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
        }

        public async Task<bool> LoadProjectFromFileAsync(string filePath)
        {
            var data = await SessionManager.LoadFromFileAsync(filePath);
            if (data != null)
            {
                bool ok = await RestoreSessionAsync(data);
                if (ok)
                {
                    CurrentProjectPath = filePath;
                    StatusText = $"已打开项目工程: {Path.GetFileName(filePath)}";
                    return true;
                }
            }
            return false;
        }

        public void AttachPhotoChangeHandler(PhotoViewModel photo)
        {
            photo.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(PhotoViewModel.OffsetX)
                    or nameof(PhotoViewModel.OffsetY)
                    or nameof(PhotoViewModel.CropScale)
                    or nameof(PhotoViewModel.Mode)
                    or nameof(PhotoViewModel.Orientation))
                {
                    MarkSessionDirty();
                    RequestAutoSave();
                }
            };
        }

        public ICommand ExportCommand { get; }
        public ICommand CancelExportCommand { get; }
        public ICommand SwitchViewModeCommand { get; }
        public ICommand ToggleModeCommand { get; }
        public ICommand SetModeCommand { get; }
        public ICommand ToggleOrientationCommand { get; }
        public ICommand ResetCropCommand { get; }
        public ICommand OpenFileInExplorerCommand { get; }
        public ICommand CopyFilePathCommand { get; }

        public void ToggleViewMode() =>
            CurrentViewMode = CurrentViewMode == ViewMode.Single ? ViewMode.Batch : ViewMode.Single;

        /// <summary>
        /// 按 <paramref name="direction"/>（-1 上一张 / +1 下一张）切换照片。
        /// <para>
        /// 严格单向查找，<b>绝不绕回另一端</b>：已在首/尾时返回边界状态并写入
        /// <see cref="StatusText"/> 提示用户；淡化/隐藏免修模式下若该方向只剩免修照片，
        /// 同样停止并给出提示。
        /// </para>
        /// </summary>
        public PhotoNavigationStatus NavigateRelative(int direction)
        {
            if (Photos.Count == 0)
            {
                return PhotoNavigationStatus.Empty;
            }

            int step = direction >= 0 ? 1 : -1;
            int current = SelectedPhoto != null ? Photos.IndexOf(SelectedPhoto) : -1;

            if (current < 0)
            {
                SelectedPhoto = Photos[0];
                return PhotoNavigationStatus.Moved;
            }

            bool skipMatched = _matchedFilterMode != PhotoFilterMode.Show;
            for (int i = current + step; i >= 0 && i < Photos.Count; i += step)
            {
                if (skipMatched && Photos[i].IsAspectMatched)
                {
                    continue;
                }

                SelectedPhoto = Photos[i];
                return PhotoNavigationStatus.Moved;
            }

            bool atBoundary = step > 0 ? current >= Photos.Count - 1 : current <= 0;
            if (atBoundary)
            {
                PhotoNavigationStatus boundary = step > 0
                    ? PhotoNavigationStatus.ReachedLast
                    : PhotoNavigationStatus.ReachedFirst;
                StatusText = DescribeNavigationStatus(boundary);
                return boundary;
            }

            StatusText = DescribeNavigationStatus(PhotoNavigationStatus.NoActionableInDirection);
            return PhotoNavigationStatus.NoActionableInDirection;
        }

        /// <summary>把导航状态转成给用户看的提示文案（无提示时返回空串）。</summary>
        public static string DescribeNavigationStatus(PhotoNavigationStatus status) => status switch
        {
            PhotoNavigationStatus.ReachedFirst => "已经是第一张照片了",
            PhotoNavigationStatus.ReachedLast => "已经是最后一张照片了",
            PhotoNavigationStatus.NoActionableInDirection => "该方向已经没有需要调整的照片了",
            PhotoNavigationStatus.Empty => "还没有导入任何照片",
            _ => ""
        };

        public void ResetCrop(System.Collections.IList? targetPhotos = null)
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
                // 重置构图对两种裁切模式同时生效，避免切换模式后又冒出一份旧构图
                photo.ResetModeOffsets();
                photo.CropScale = 1.0;
            }

            StatusText = $"已恢复 {list.Count} 张照片的默认剪切构图";
        }

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

        public MainViewModel(IImageProcessor? imageProcessor = null, string? autoSaveFilePath = null)
        {
            _imageProcessor = imageProcessor ?? ImageProcessor.Instance;
            _autoSaveFilePath = autoSaveFilePath ?? SessionManager.AutoSaveFilePath;
            _selectedSize = Sizes.FirstOrDefault(s => s.Name == "6寸") ?? PhotoSize.Inch6;

            RestoreDraftCommand = new RelayCommand(async _ => await RestoreAutoSaveDraftAsync());
            DiscardDraftCommand = new RelayCommand(_ => DiscardAutoSaveDraft());

            ResetCropCommand = new RelayCommand(p =>
            {
                if (p is System.Collections.IList list)
                    ResetCrop(list);
                else
                    ResetCrop();
            });

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
                async _ => await ExportAsync(),
                _ => !IsExporting && Photos.Count > 0
            );

            CancelExportCommand = new RelayCommand(
                _ => _exportCts?.Cancel(),
                _ => IsExporting
            );

            _preExporter = new PipelinePreExporter(_imageProcessor);
            _preExporter.PreExportProgressChanged += (ready, total) =>
            {
                string text = total <= 0 || ready <= 0
                    ? ""
                    : ready >= total
                        ? $"预导出就绪 {ready}/{total} 张"
                        : $"已预导出 {ready}/{total} 张";

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.InvokeAsync(() => PreExportStatusText = text);
                }
                else
                {
                    PreExportStatusText = text;
                }
            };

            _preExporter.Start(
                SnapshotPreExportItems,
                SnapshotCurrentPreExportItem,
                () => IsExporting
            );
        }

        /// <summary>
        /// 在 UI 线程上对照片集合做快照，供后台预导出线程使用。
        /// <para>
        /// 后台直接枚举 <see cref="Photos"/> 会与批量导入时 UI 线程的分批 Add 冲突，
        /// 抛出"集合已被修改"并让预导出循环失效，因此必须回到 UI 线程取快照。
        /// </para>
        /// </summary>
        private IReadOnlyList<PreExportItem> SnapshotPreExportItems()
        {
            return RunOnUiThread(() => (IReadOnlyList<PreExportItem>)Photos
                .Select(p => new PreExportItem(
                    p.FilePath,
                    p.OffsetX,
                    p.OffsetY,
                    p.CropScale,
                    p.Mode,
                    p.Orientation,
                    SelectedSize,
                    p.LastModifiedUtc))
                .ToList());
        }

        private PreExportItem? SnapshotCurrentPreExportItem()
        {
            return RunOnUiThread(() =>
            {
                var photo = SelectedPhoto;
                return photo == null
                    ? (PreExportItem?)null
                    : new PreExportItem(
                        photo.FilePath,
                        photo.OffsetX,
                        photo.OffsetY,
                        photo.CropScale,
                        photo.Mode,
                        photo.Orientation,
                        SelectedSize,
                        photo.LastModifiedUtc);
            });
        }

        private static T RunOnUiThread<T>(Func<T> factory)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return factory();
            }

            return dispatcher.Invoke(factory);
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
            OnPropertyChanged(nameof(HasPhotos));
            OnPropertyChanged(nameof(TotalPhotosCount));
            OnPropertyChanged(nameof(CurrentPhotoIndex));
            OnPropertyChanged(nameof(OverallProgressPercent));
            OnPropertyChanged(nameof(OverallProgressText));

            // 先构建壳列表，不立刻添加到 Photos（避免 WPF 视觉树构建堵死 UI 队列）
            var photoList = allFiles.Select(f => new PhotoViewModel(f)).ToList();

            int total = photoList.Count;
            int loaded = 0;

            var progress = new Progress<(int current, double pct, string name)>(report =>
            {
                LoadingProgress = report.pct;
                LoadingStatusText = $"正在载入 {report.current}/{total}";
            });

            long lastReportTick = 0;
            await Task.Run(() =>
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
                Parallel.ForEach(photoList, options, photo =>
                {
                    LoadThumbnail(photo);

                    int current = Interlocked.Increment(ref loaded);
                    double pct = (double)current / total * 100.0;

                    // 节流：最多每 50ms 上报一次，但最后一张必须上报
                    bool isLast = current == total;
                    long now = Environment.TickCount64;
                    long last = Interlocked.Read(ref lastReportTick);
                    if (isLast || now - last >= 50)
                    {
                        if (isLast || Interlocked.CompareExchange(ref lastReportTick, now, last) == last)
                        {
                            string name = Path.GetFileName(photo.FilePath);
                            ((IProgress<(int, double, string)>)progress).Report((current, pct, name));
                        }
                    }
                });
            });

            // 确保后台全部完成后进度显示立刻精确到达 100% (total/total)
            LoadingProgress = 100.0;
            LoadingStatusText = $"已载入缩略图 ({total}/{total})，正在构建视口...";
            await Task.Delay(1);

            // 分批平滑推入 ObservableCollection，每批出让 UI 线程让 Dispatcher 渲染，彻底消除假死
            await StreamPopulatePhotosAsync(photoList);

            IsLoading = false;
            IsInitialLoading = false;
            OnPropertyChanged(nameof(IsDropOverlayVisible));
            UpdateAspectMatchForAll();
            StatusText = $"已导入 {Photos.Count} 张照片";
            OnPropertyChanged(nameof(HasPhotos));
            OnPropertyChanged(nameof(TotalPhotosCount));
            OnPropertyChanged(nameof(CurrentPhotoIndex));
            OnPropertyChanged(nameof(OverallProgressPercent));
            OnPropertyChanged(nameof(OverallProgressText));

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

            MarkSessionDirty();
            RequestAutoSave();
        }

        private async Task StreamPopulatePhotosAsync(List<PhotoViewModel> photoList)
        {
            int total = photoList.Count;
            const int batchSize = 35;
            for (int i = 0; i < total; i += batchSize)
            {
                int count = Math.Min(batchSize, total - i);
                for (int j = 0; j < count; j++)
                {
                    var p = photoList[i + j];
                    AttachPhotoChangeHandler(p);
                    Photos.Add(p);
                }

                int currentCount = i + count;
                LoadingProgress = (double)currentCount / total * 100.0;
                LoadingStatusText = $"正在构建视口卡片 ({currentCount}/{total})...";
                await Task.Delay(1);
            }

            LoadingProgress = 100.0;
            LoadingStatusText = $"已完成全部 {total} 张照片载入 (100%)";
            await Task.Delay(150);
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

        /// <summary>
        /// 执行批量导出。全部照片导出成功后，工作区被视为已交付，草稿文件随之清理。
        /// </summary>
        public async Task ExportAsync()
        {
            if (!Photos.Any())
            {
                StatusText = "请先添加图片";
                return;
            }

            string firstFileDir = Path.GetDirectoryName(Photos[0].FilePath) ?? "";
            string outputFolder = Path.Combine(firstFileDir, "Clipped_Photos");

            var exportItems = Photos
                .Select(p => new PhotoExportItem(p.FilePath, p.OffsetX, p.OffsetY, p.Mode, p.Orientation, p.CropScale))
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
                    _exportCts.Token,
                    cachedFileResolver: item =>
                    {
                        var effectiveMode = item.Mode ?? cropSettings.Mode;
                        var effectiveOrientation = item.Orientation ?? TargetOrientation.Landscape;
                        if (_preExporter.TryGetCachedExport(
                            item.FilePath,
                            cropSettings.TargetSize,
                            effectiveMode,
                            effectiveOrientation,
                            item.OffsetX,
                            item.OffsetY,
                            item.CropScale,
                            out var cachedPath))
                        {
                            return cachedPath;
                        }
                        return null;
                    }
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
                    _preExporter.MarkCompleted(Photos.Select(p => new PreExportItem(
                        p.FilePath,
                        p.OffsetX,
                        p.OffsetY,
                        p.CropScale,
                        p.Mode,
                        p.Orientation,
                        SelectedSize,
                        p.LastModifiedUtc
                    )));
                    PreExportStatusText = "";
                    MarkSessionExported();
                }
            }
            finally
            {
                IsExporting = false;
                _exportCts.Dispose();
                _exportCts = null;
            }
        }

        public void Dispose()
        {
            _autoSaveTimer?.Dispose();
            _preExporter.Dispose();
            _exportCts?.Dispose();
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
