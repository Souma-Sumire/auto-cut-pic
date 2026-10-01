using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
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

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        AllowDrop = true;
        Drop += MainWindow_Drop;
        KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        var gallery = (ListBox)FindName("PhotoGallery");
        var selectedItems = (System.Collections.IList)gallery.SelectedItems;
        if (selectedItems == null || selectedItems.Count == 0)
            return;

        bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        // 基础步进 0.01，按下 Shift 提升 10 倍 (0.1)
        double step = isShift ? 0.1 : 0.01;

        switch (e.Key)
        {
            // WASD 与方向键统一处理
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
