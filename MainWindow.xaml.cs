using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoCutPic.Core;
using AutoCutPic.ViewModels;

namespace AutoCutPic;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartOffsetX;
    private double _dragStartOffsetY;
    private string _activeHandleTag = "All";

    // 鼠标中键自动平滑滚动系统 (类似 Windows 资源管理器)
    private bool _isMiddleAutoScrolling;
    private Point _autoScrollOrigin;
    private DateTime _middlePressTime;
    private ScrollViewer? _galleryScrollViewer;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        AllowDrop = true;
        Drop += MainWindow_Drop;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        PreviewMouseUp += MainWindow_PreviewMouseUp;
        Deactivated += (_, _) => StopAutoScroll();

        Loaded += (_, _) =>
        {
            Focus();
            ScrollSelectedPhotoIntoView();
        };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedPhoto) ||
                args.PropertyName == nameof(MainViewModel.CurrentViewMode))
            {
                ScrollSelectedPhotoIntoView();
            }
        };
        Closed += (_, _) => Application.Current?.Shutdown();
    }

    private void CropHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _viewModel.SelectedPhoto != null)
        {
            if (_viewModel.SelectedMode == CutMode.Fit)
                return;

            _isDragging = true;
            _activeHandleTag = (sender as FrameworkElement)?.Tag?.ToString() ?? "All";
            _dragStartPoint = e.GetPosition(this);
            _dragStartOffsetX = _viewModel.SelectedPhoto.OffsetX;
            _dragStartOffsetY = _viewModel.SelectedPhoto.OffsetY;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }
    }

    private void CropHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _viewModel.SelectedPhoto == null)
            return;

        var host = (FrameworkElement)FindName("MainViewportHost");
        if (host == null || host.ActualWidth <= 40 || host.ActualHeight <= 40)
            return;

        var photo = _viewModel.SelectedPhoto;
        var geo = PsWorkbenchMath.Calculate(
            host.ActualWidth,
            host.ActualHeight,
            photo.OriginalWidth > 0 ? photo.OriginalWidth : 1,
            photo.OriginalHeight > 0 ? photo.OriginalHeight : 1,
            photo.OffsetX,
            photo.OffsetY,
            _viewModel.SelectedSize,
            _viewModel.SelectedMode
        );

        Point current = e.GetPosition(this);
        double deltaX = current.X - _dragStartPoint.X;
        double deltaY = current.Y - _dragStartPoint.Y;

        bool allowX = _activeHandleTag is "All" or "Left" or "Right" or "TopLeft" or "TopRight" or "BottomLeft" or "BottomRight";
        bool allowY = _activeHandleTag is "All" or "Top" or "Bottom" or "TopLeft" or "TopRight" or "BottomLeft" or "BottomRight";

        if (allowX && geo.ExcessW > 1)
        {
            double newOx = _dragStartOffsetX + (deltaX / geo.ExcessW);
            photo.OffsetX = Math.Clamp(newOx, -0.5, 0.5);
        }

        if (allowY && geo.ExcessH > 1)
        {
            double newOy = _dragStartOffsetY + (deltaY / geo.ExcessH);
            photo.OffsetY = Math.Clamp(newOy, -0.5, 0.5);
        }
    }

    private void CropHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void PhotoCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _viewModel.SelectedPhoto != null)
        {
            if (_viewModel.SelectedMode == CutMode.Fit)
                return;

            _isDragging = true;
            _activeHandleTag = "All";
            _dragStartPoint = e.GetPosition(this);
            _dragStartOffsetX = _viewModel.SelectedPhoto.OffsetX;
            _dragStartOffsetY = _viewModel.SelectedPhoto.OffsetY;
            ((UIElement)sender).CaptureMouse();
        }
    }

    private void PhotoCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        CropHandle_MouseMove(sender, e);
    }

    private void PhotoCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }
    }

    private void BatchGalleryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedPhoto != null)
        {
            _viewModel.CurrentViewMode = ViewMode.Single;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isMiddleAutoScrolling)
        {
            StopAutoScroll();
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                return;
            }
        }

        Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        // G: 切换批量画廊 / 单张精调视图
        if (key == Key.G)
        {
            if (_viewModel.Photos.Any())
            {
                _viewModel.ToggleViewMode();
            }
            e.Handled = true;
            return;
        }

        // Esc: 单张精调视图下按 Esc 返回批量画廊
        if (key == Key.Escape)
        {
            if (_viewModel.CurrentViewMode == ViewMode.Single && _viewModel.Photos.Any())
            {
                _viewModel.CurrentViewMode = ViewMode.Batch;
                e.Handled = true;
                return;
            }
        }

        // Enter: 批量画廊视图下，按 Enter 进入单张精调当前选中的照片
        if (key == Key.Enter)
        {
            if (_viewModel.CurrentViewMode == ViewMode.Batch && _viewModel.SelectedPhoto != null)
            {
                _viewModel.CurrentViewMode = ViewMode.Single;
                e.Handled = true;
                return;
            }
        }

        // Q/E 快捷切换上一张/下一张
        if (key == Key.Q)
        {
            SelectRelativePhoto(-1);
            e.Handled = true;
            return;
        }
        if (key == Key.E)
        {
            SelectRelativePhoto(1);
            e.Handled = true;
            return;
        }

        var batchGallery = (ListBox?)FindName("BatchGalleryList");
        System.Collections.IList? selectedItems = batchGallery?.SelectedItems?.Count > 0
            ? batchGallery.SelectedItems
            : (_viewModel.SelectedPhoto != null ? new List<PhotoViewModel> { _viewModel.SelectedPhoto } : null);

        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        // 基础步进 0.01，按下 Shift 提升 10 倍 (0.1)
        double step = isShift ? 0.1 : 0.01;

        switch (key)
        {
            case Key.W:
            case Key.Up:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, AlignmentDirection.Top);
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, 0, -step);
                e.Handled = true;
                break;
            case Key.S:
            case Key.Down:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, AlignmentDirection.Bottom);
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, 0, step);
                e.Handled = true;
                break;
            case Key.A:
            case Key.Left:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, AlignmentDirection.Left);
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, -step, 0);
                e.Handled = true;
                break;
            case Key.D:
            case Key.Right:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, AlignmentDirection.Right);
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, step, 0);
                e.Handled = true;
                break;

            case Key.F:
                var targetMode = (_viewModel.SelectedPhoto?.Mode ?? CutMode.Fill) == CutMode.Fill
                    ? CutMode.Fit
                    : CutMode.Fill;
                _viewModel.SetModeBatch(selectedItems, targetMode);
                e.Handled = true;
                break;
        }
    }

    private void SelectRelativePhoto(int direction)
    {
        if (!_viewModel.Photos.Any())
            return;

        int currentIndex = _viewModel.SelectedPhoto != null ? _viewModel.Photos.IndexOf(_viewModel.SelectedPhoto) : 0;
        int step = direction > 0 ? 1 : -1;

        if (_viewModel.DimMatchedPhotos && _viewModel.Photos.Any(p => !p.IsAspectMatched))
        {
            // 当开启淡化跳过开关时，向目标方向搜寻下一个未匹配同比例（需要裁切调整）的照片
            int count = _viewModel.Photos.Count;
            int targetIndex = currentIndex;
            for (int i = 1; i <= count; i++)
            {
                int candidate = (currentIndex + (step * i) % count + count) % count;
                if (!_viewModel.Photos[candidate].IsAspectMatched)
                {
                    targetIndex = candidate;
                    break;
                }
            }
            _viewModel.SelectedPhoto = _viewModel.Photos[targetIndex];
        }
        else
        {
            int newIndex = Math.Clamp(currentIndex + step, 0, _viewModel.Photos.Count - 1);
            _viewModel.SelectedPhoto = _viewModel.Photos[newIndex];
        }

        ScrollSelectedPhotoIntoView();
    }

    private void ScrollSelectedPhotoIntoView()
    {
        var selected = _viewModel.SelectedPhoto;
        if (selected == null)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            var batchGallery = (ListBox?)FindName("BatchGalleryList");
            if (batchGallery != null && batchGallery.IsVisible)
            {
                batchGallery.ScrollIntoView(selected);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var allFiles = GetAllFiles(droppedPaths).ToArray();

            if (allFiles.Length > 0)
            {
                await _viewModel.LoadFiles(allFiles);
                Focus();
                ScrollSelectedPhotoIntoView();
            }
        }
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择照片文件夹（支持自动递归扫描所有子目录）"
        };

        if (dialog.ShowDialog() == true)
        {
            var allFiles = GetAllFiles(new[] { dialog.FolderName }).ToArray();
            if (allFiles.Length > 0)
            {
                await _viewModel.LoadFiles(allFiles);
                Focus();
                ScrollSelectedPhotoIntoView();
            }
        }
    }

    private async void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择单张或多张照片文件",
            Multiselect = true,
            Filter = "支持的照片格式 (*.jpg;*.jpeg;*.png;*.webp;*.heic;*.zip;*.tiff;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.zip;*.tiff;*.bmp|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var allFiles = GetAllFiles(dialog.FileNames).ToArray();
            if (allFiles.Length > 0)
            {
                await _viewModel.LoadFiles(allFiles);
                Focus();
                ScrollSelectedPhotoIntoView();
            }
        }
    }

    private static IEnumerable<string> GetAllFiles(string[] paths)
    {
        var extensions = new[]
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".heic",
            ".zip",
            ".tiff",
            ".bmp",
        };
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    if (extensions.Contains(Path.GetExtension(file).ToLower()))
                        yield return file;
                }
            }
            else if (File.Exists(path))
            {
                if (extensions.Contains(Path.GetExtension(path).ToLower()))
                    yield return path;
            }
        }
    }

    #region 鼠标中键自动平滑滚动 (Auto-Scroll)

    private void BatchGalleryList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (_isMiddleAutoScrolling)
            {
                StopAutoScroll();
                e.Handled = true;
                return;
            }

            StartAutoScroll(e);
            e.Handled = true;
        }
        else if (_isMiddleAutoScrolling)
        {
            StopAutoScroll();
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isMiddleAutoScrolling)
        {
            if (e.ChangedButton != MouseButton.Middle || (DateTime.UtcNow - _middlePressTime).TotalMilliseconds > 150)
            {
                StopAutoScroll();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isMiddleAutoScrolling && e.ChangedButton == MouseButton.Middle)
        {
            Point currentPos = e.GetPosition(this);
            double dist = (currentPos - _autoScrollOrigin).Length;
            double elapsedMs = (DateTime.UtcNow - _middlePressTime).TotalMilliseconds;

            // 若中键长按超过 250ms 或已发生明确拖拽位移 (> 8px)，松开即结束滚动
            if (elapsedMs > 250 || dist > 8.0)
            {
                StopAutoScroll();
                e.Handled = true;
            }
        }
    }

    private void StartAutoScroll(MouseButtonEventArgs e)
    {
        var batchGallery = (ListBox?)FindName("BatchGalleryList");
        if (batchGallery == null)
            return;

        _galleryScrollViewer ??= FindVisualChild<ScrollViewer>(batchGallery);
        if (_galleryScrollViewer == null)
            return;

        _isMiddleAutoScrolling = true;
        _middlePressTime = DateTime.UtcNow;
        _autoScrollOrigin = e.GetPosition(this);

        var anchor = (FrameworkElement?)FindName("AutoScrollAnchor");
        var canvas = (FrameworkElement?)FindName("AutoScrollCanvas");
        if (anchor != null && canvas != null)
        {
            Point canvasPos = e.GetPosition(canvas);
            Canvas.SetLeft(anchor, canvasPos.X - 16);
            Canvas.SetTop(anchor, canvasPos.Y - 16);
            anchor.Visibility = Visibility.Visible;
        }

        CaptureMouse();
        CompositionTarget.Rendering += AutoScroll_OnRendering;
    }

    private void StopAutoScroll()
    {
        if (!_isMiddleAutoScrolling)
            return;

        _isMiddleAutoScrolling = false;
        CompositionTarget.Rendering -= AutoScroll_OnRendering;

        var anchor = (FrameworkElement?)FindName("AutoScrollAnchor");
        if (anchor != null)
        {
            anchor.Visibility = Visibility.Collapsed;
        }

        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    private void AutoScroll_OnRendering(object? sender, EventArgs e)
    {
        if (!_isMiddleAutoScrolling || _galleryScrollViewer == null)
            return;

        Point currentPos = Mouse.GetPosition(this);
        double deltaY = currentPos.Y - _autoScrollOrigin.Y;
        const double deadZone = 12.0;

        if (Math.Abs(deltaY) <= deadZone)
        {
            Cursor = Cursors.ScrollNS;
            return;
        }

        if (deltaY > deadZone)
        {
            Cursor = Cursors.ScrollS;
            double dist = deltaY - deadZone;
            double speed = Math.Min(60.0, Math.Pow(dist / 8.0, 1.25));
            _galleryScrollViewer.ScrollToVerticalOffset(_galleryScrollViewer.VerticalOffset + speed);
        }
        else
        {
            Cursor = Cursors.ScrollN;
            double dist = Math.Abs(deltaY) - deadZone;
            double speed = Math.Min(60.0, Math.Pow(dist / 8.0, 1.25));
            _galleryScrollViewer.ScrollToVerticalOffset(_galleryScrollViewer.VerticalOffset - speed);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
            return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

    #endregion
}
