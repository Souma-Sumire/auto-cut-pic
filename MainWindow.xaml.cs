using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        AllowDrop = true;
        Drop += MainWindow_Drop;
        KeyDown += MainWindow_KeyDown;
    }

    private void PhotoCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _viewModel.SelectedPhoto != null)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            _dragStartOffsetX = _viewModel.SelectedPhoto.OffsetX;
            _dragStartOffsetY = _viewModel.SelectedPhoto.OffsetY;
            ((UIElement)sender).CaptureMouse();
        }
    }

    private void PhotoCanvas_MouseMove(object sender, MouseEventArgs e)
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

        if (geo.ExcessW > 1)
        {
            double newOx = _dragStartOffsetX + (deltaX / geo.ExcessW);
            photo.OffsetX = Math.Clamp(newOx, -0.5, 0.5);
        }

        if (geo.ExcessH > 1)
        {
            double newOy = _dragStartOffsetY + (deltaY / geo.ExcessH);
            photo.OffsetY = Math.Clamp(newOy, -0.5, 0.5);
        }
    }

    private void PhotoCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        var filmstrip = (ListBox)FindName("FilmstripList");
        var selectedItems = (System.Collections.IList)filmstrip.SelectedItems;

        bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        // Q/E 快捷切换上一张/下一张
        if (e.Key == Key.Q)
        {
            SelectRelativePhoto(-1);
            return;
        }
        if (e.Key == Key.E)
        {
            SelectRelativePhoto(1);
            return;
        }

        if (selectedItems == null || selectedItems.Count == 0)
        {
            if (_viewModel.SelectedPhoto != null)
                selectedItems = new List<PhotoViewModel> { _viewModel.SelectedPhoto };
            else
                return;
        }

        // 基础步进 0.01，按下 Shift 提升 10 倍 (0.1)
        double step = isShift ? 0.1 : 0.01;

        switch (e.Key)
        {
            case Key.W:
            case Key.Up:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, "top");
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, 0, -step);
                break;
            case Key.S:
            case Key.Down:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, "bottom");
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, 0, step);
                break;
            case Key.A:
            case Key.Left:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, "left");
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, -step, 0);
                break;
            case Key.D:
            case Key.Right:
                if (isCtrl)
                    _viewModel.AlignBatch(selectedItems, "right");
                else
                    _viewModel.AdjustOffsetBatch(selectedItems, step, 0);
                break;

            case Key.Space:
                _viewModel.SelectedMode =
                    _viewModel.SelectedMode == CutMode.Fill ? CutMode.Fit : CutMode.Fill;
                break;

            case Key.Enter:
                _viewModel.ExportCommand.Execute(null);
                break;
        }
    }

    private void SelectRelativePhoto(int offset)
    {
        if (!_viewModel.Photos.Any())
            return;

        int currentIndex = _viewModel.SelectedPhoto != null ? _viewModel.Photos.IndexOf(_viewModel.SelectedPhoto) : 0;
        int newIndex = Math.Clamp(currentIndex + offset, 0, _viewModel.Photos.Count - 1);
        _viewModel.SelectedPhoto = _viewModel.Photos[newIndex];

        var filmstrip = (ListBox)FindName("FilmstripList");
        filmstrip?.ScrollIntoView(_viewModel.SelectedPhoto);
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
}
