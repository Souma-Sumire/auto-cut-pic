using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoCutPic.Core;
using AutoCutPic.ViewModels;
using ImageMagick;
using Xunit;

namespace AutoCutPic.Tests
{
    public class MainViewModelTests : IDisposable
    {
        private readonly string _testDir;

        public MainViewModelTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_VMTests_" + Guid.NewGuid().ToString("N"));
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

        [Fact]
        public async Task LoadFiles_ShouldStreamPhotosAndResetLoadingState()
        {
            // 准备 2 张测试图片
            string f1 = Path.Combine(_testDir, "p1.jpg");
            string f2 = Path.Combine(_testDir, "p2.jpg");

            using (var m1 = new MagickImage(MagickColors.Red, 200, 200))
            {
                m1.Write(f1);
            }
            using (var m2 = new MagickImage(MagickColors.Blue, 200, 200))
            {
                m2.Write(f2);
            }

            var vm = new MainViewModel();
            Assert.False(vm.IsLoading);
            Assert.Empty(vm.Photos);

            await vm.LoadFiles(new[] { f1, f2 });

            Assert.False(vm.IsLoading);
            Assert.Equal(2, vm.Photos.Count);
            Assert.NotNull(vm.SelectedPhoto);
            Assert.Equal(f1, vm.SelectedPhoto.FilePath);
        }

        [Fact]
        public async Task LoadFiles_WhenFirstPhotoIsMatched_InDefaultDimMode_ShouldSelectFirstActionablePhoto()
        {
            // 准备 2 张测试图片：f1 为 6 寸同比例 (300x200)，f2 为正方形需裁切 (200x200)
            string f1 = Path.Combine(_testDir, "matched.jpg");
            string f2 = Path.Combine(_testDir, "unmatched.jpg");

            using (var m1 = new MagickImage(MagickColors.Red, 300, 200))
            {
                m1.Write(f1);
            }
            using (var m2 = new MagickImage(MagickColors.Blue, 200, 200))
            {
                m2.Write(f2);
            }

            var vm = new MainViewModel();
            Assert.True(vm.DimMatchedPhotos); // 默认淡化免修模式

            await vm.LoadFiles(new[] { f1, f2 });

            // f1 为同比例淡化照片，f2 为非淡化需要构图照片
            Assert.True(vm.Photos[0].IsAspectMatched);
            Assert.False(vm.Photos[1].IsAspectMatched);

            // 必须默认自动聚焦选中第一张需要人工构图干预的非淡化照片 f2
            Assert.NotNull(vm.SelectedPhoto);
            Assert.Equal(f2, vm.SelectedPhoto.FilePath);
        }

        [Fact]
        public void MatchedFilterMode_SwitchFromShowToDim_ShouldSelectFirstActionablePhotoIfCurrentIsMatched()
        {
            var p1 = new PhotoViewModel("p1.jpg") { IsAspectMatched = true };
            var p2 = new PhotoViewModel("p2.jpg") { IsAspectMatched = false };

            var vm = new MainViewModel();
            vm.Photos.Add(p1);
            vm.Photos.Add(p2);

            // 在 Show 模式下选中了匹配照片 p1
            vm.ShowAllPhotos = true;
            vm.SelectedPhoto = p1;
            Assert.Equal(p1, vm.SelectedPhoto);

            // 切换为默认淡化模式
            vm.DimMatchedPhotos = true;

            // 自动跳过淡化照片，切换到第一张非淡化待修照片 p2
            Assert.Equal(p2, vm.SelectedPhoto);
        }

        [Fact]
        public void AlignBatch_ShouldSetCorrectClampedOffsets()
        {
            var p1 = new PhotoViewModel("dummy1.jpg") { OffsetX = 0, OffsetY = 0 };
            var p2 = new PhotoViewModel("dummy2.jpg") { OffsetX = 0, OffsetY = 0 };
            var list = new List<PhotoViewModel> { p1, p2 };

            var vm = new MainViewModel();

            vm.AlignBatch(list, AlignmentDirection.Top);
            Assert.Equal(-0.5, p1.OffsetY);
            Assert.Equal(-0.5, p2.OffsetY);

            vm.AlignBatch(list, AlignmentDirection.Right);
            Assert.Equal(0.5, p1.OffsetX);
            Assert.Equal(0.5, p2.OffsetX);

            vm.AlignBatch(list, AlignmentDirection.Center);
            Assert.Equal(0.0, p1.OffsetX);
            Assert.Equal(0.0, p2.OffsetY);
        }

        [Fact]
        public void PhotoViewModel_Mode_ShouldBeIndependentAndFollowSelectedPhoto()
        {
            var p1 = new PhotoViewModel("dummy1.jpg") { Mode = CutMode.Fill };
            var p2 = new PhotoViewModel("dummy2.jpg") { Mode = CutMode.Fit };
            var list = new List<PhotoViewModel> { p1, p2 };

            var vm = new MainViewModel();
            foreach (var p in list)
            {
                vm.Photos.Add(p);
            }

            vm.SelectedPhoto = p1;
            Assert.Equal(CutMode.Fill, vm.SelectedMode);

            vm.SelectedPhoto = p2;
            Assert.Equal(CutMode.Fit, vm.SelectedMode);

            // 修改 SelectedMode 仅改变当前选中照片
            vm.SelectedMode = CutMode.Fill;
            Assert.Equal(CutMode.Fill, p2.Mode);
            Assert.Equal(CutMode.Fill, p1.Mode);

            // 批量将 p1 改为 Fit，p2 不受影响
            vm.SetModeBatch(new[] { p1 }, CutMode.Fit);
            Assert.Equal(CutMode.Fit, p1.Mode);
            Assert.Equal(CutMode.Fill, p2.Mode);
        }

        [Fact]
        public void ToggleModeCommand_And_SetModeCommand_ShouldUpdateCurrentPhoto()
        {
            var p = new PhotoViewModel("test.jpg") { Mode = CutMode.Fill };
            var vm = new MainViewModel();
            vm.Photos.Add(p);
            vm.SelectedPhoto = p;

            Assert.Equal(CutMode.Fill, vm.SelectedMode);

            // 执行 ToggleModeCommand 翻转为 Fit
            vm.ToggleModeCommand.Execute(null);
            Assert.Equal(CutMode.Fit, vm.SelectedMode);
            Assert.Equal(CutMode.Fit, p.Mode);

            // 再次执行 ToggleModeCommand 翻转为 Fill
            vm.ToggleModeCommand.Execute(null);
            Assert.Equal(CutMode.Fill, vm.SelectedMode);
            Assert.Equal(CutMode.Fill, p.Mode);

            // 执行 SetModeCommand
            vm.SetModeCommand.Execute("Fit");
            Assert.Equal(CutMode.Fit, vm.SelectedMode);
            Assert.Equal(CutMode.Fit, p.Mode);
        }

        [Fact]
        public void ToggleOrientation_ShouldToggleLandscapeAndPortrait()
        {
            var p = new PhotoViewModel("test.jpg")
            {
                OriginalWidth = 1000,
                OriginalHeight = 2000,
                Orientation = AutoCutPic.Core.Calculators.TargetOrientation.Portrait
            };
            var vm = new MainViewModel();
            vm.Photos.Add(p);
            vm.SelectedPhoto = p;

            // 单张通过快捷命令或方法翻转
            vm.ToggleOrientation();
            Assert.Equal(AutoCutPic.Core.Calculators.TargetOrientation.Landscape, p.Orientation);

            vm.ToggleOrientation();
            Assert.Equal(AutoCutPic.Core.Calculators.TargetOrientation.Portrait, p.Orientation);
        }

        [Fact]
        public void MatchedFilterMode_RadioSelection_ShouldMutuallyExclude()
        {
            var vm = new MainViewModel();

            // 默认淡化模式
            Assert.True(vm.DimMatchedPhotos);
            Assert.False(vm.HideMatchedPhotos);
            Assert.False(vm.ShowAllPhotos);
            Assert.Equal(MainViewModel.PhotoFilterMode.Dim, vm.MatchedFilterMode);

            // 切换为隐藏
            vm.HideMatchedPhotos = true;
            Assert.False(vm.DimMatchedPhotos);
            Assert.True(vm.HideMatchedPhotos);
            Assert.False(vm.ShowAllPhotos);
            Assert.Equal(MainViewModel.PhotoFilterMode.Hide, vm.MatchedFilterMode);

            // 切换为全部显示
            vm.ShowAllPhotos = true;
            Assert.False(vm.DimMatchedPhotos);
            Assert.False(vm.HideMatchedPhotos);
            Assert.True(vm.ShowAllPhotos);
            Assert.Equal(MainViewModel.PhotoFilterMode.Show, vm.MatchedFilterMode);
        }

        [Fact]
        public void GalleryCardWidth_ShouldClampBetweenMinAndMaxAndSyncHeights()
        {
            var vm = new MainViewModel();

            // 默认 130
            Assert.Equal(130.0, vm.GalleryCardWidth);
            Assert.Equal(132.0, vm.GalleryCardHeight);
            Assert.Equal(94.0, vm.GalleryImageContainerHeight);

            // 小于最小值 80 钳制为 80
            vm.GalleryCardWidth = 50.0;
            Assert.Equal(80.0, vm.GalleryCardWidth);
            Assert.Equal(96.0, vm.GalleryCardHeight);
            Assert.Equal(58.0, vm.GalleryImageContainerHeight);

            // 大于最大值 300 钳制为 300
            vm.GalleryCardWidth = 500.0;
            Assert.Equal(300.0, vm.GalleryCardWidth);
            Assert.Equal(254.0, vm.GalleryCardHeight);
            Assert.Equal(216.0, vm.GalleryImageContainerHeight);

            // 正常赋值
            vm.GalleryCardWidth = 200.0;
            Assert.Equal(200.0, vm.GalleryCardWidth);
            Assert.Equal(182.0, vm.GalleryCardHeight);
            Assert.Equal(144.0, vm.GalleryImageContainerHeight);
        }

        [Fact]
        public void IsDropOverlayVisible_ShouldBeTrueInitiallyAndFalseAfterLoadComplete()
        {
            var vm = new MainViewModel();

            // 初始无照片且未开始加载
            Assert.Empty(vm.Photos);
            Assert.False(vm.IsInitialLoading);
            Assert.True(vm.IsDropOverlayVisible);

            // 模拟开始初始加载且有照片逐步加入
            vm.IsInitialLoading = true;
            vm.Photos.Add(new PhotoViewModel("dummy.jpg"));
            // 此时虽有照片，但仍处于初始加载阶段，遮罩与全局进度条必须保持可见
            Assert.True(vm.IsDropOverlayVisible);

            // 加载彻底完成
            vm.IsInitialLoading = false;
            // 此时有照片且初始加载完成，遮罩隐藏，大图工作台与平铺列表展露
            Assert.False(vm.IsDropOverlayVisible);

            // 当用户清空列表时，遮罩重新展现
            vm.Photos.Clear();
            Assert.True(vm.IsDropOverlayVisible);
        }

        [Fact]
        public void GalleryCommands_CanExecute_ShouldValidatePhotoAvailability()
        {
            var vm = new MainViewModel();

            Assert.False(vm.OpenFileInExplorerCommand.CanExecute(null));
            Assert.False(vm.CopyFilePathCommand.CanExecute(null));

            var p = new PhotoViewModel("dummy.jpg");
            Assert.True(vm.OpenFileInExplorerCommand.CanExecute(p));
            Assert.True(vm.CopyFilePathCommand.CanExecute(p));
        }

        [Fact]
        public void OverallProgress_ShouldCalculateCorrectly()
        {
            var vm = new MainViewModel();
            Assert.False(vm.HasPhotos);
            Assert.Equal(0, vm.CurrentPhotoIndex);
            Assert.Equal(0, vm.TotalPhotosCount);
            Assert.Equal(0.0, vm.OverallProgressPercent);
            Assert.Equal("0 / 0 (0%)", vm.OverallProgressText);

            var p1 = new PhotoViewModel("1.jpg");
            var p2 = new PhotoViewModel("2.jpg");
            var p3 = new PhotoViewModel("3.jpg");
            var p4 = new PhotoViewModel("4.jpg");

            vm.Photos.Add(p1);
            vm.Photos.Add(p2);
            vm.Photos.Add(p3);
            vm.Photos.Add(p4);

            vm.SelectedPhoto = p1;
            Assert.True(vm.HasPhotos);
            Assert.Equal(1, vm.CurrentPhotoIndex);
            Assert.Equal(4, vm.TotalPhotosCount);
            Assert.Equal(25.0, vm.OverallProgressPercent);
            Assert.Equal("1 / 4 (25%)", vm.OverallProgressText);

            vm.SelectedPhoto = p3;
            Assert.Equal(3, vm.CurrentPhotoIndex);
            Assert.Equal(75.0, vm.OverallProgressPercent);
            Assert.Equal("3 / 4 (75%)", vm.OverallProgressText);

            vm.SelectedPhoto = p4;
            Assert.Equal(4, vm.CurrentPhotoIndex);
            Assert.Equal(100.0, vm.OverallProgressPercent);
            Assert.Equal("4 / 4 (100%)", vm.OverallProgressText);
        }

        [Fact]
        public void CreateSessionSnapshot_ShouldRecordAccurateState()
        {
            var vm = new MainViewModel();
            var p1 = new PhotoViewModel("test1.jpg")
            {
                OffsetX = 0.25,
                OffsetY = -0.15,
                CropScale = 0.85,
                Mode = CutMode.Fit,
                Orientation = AutoCutPic.Core.Calculators.TargetOrientation.Portrait,
                IsAspectMatched = false
            };
            vm.Photos.Add(p1);
            vm.SelectedPhoto = p1;

            var snapshot = vm.CreateSessionSnapshot();
            Assert.NotNull(snapshot);
            Assert.Single(snapshot.Photos);
            Assert.Equal("6寸", snapshot.TargetSizeName);
            Assert.Equal("test1.jpg", snapshot.Photos[0].FilePath);
            Assert.Equal(0.25, snapshot.Photos[0].OffsetX);
            Assert.Equal(-0.15, snapshot.Photos[0].OffsetY);
            Assert.Equal(0.85, snapshot.Photos[0].CropScale);
            Assert.Equal(CutMode.Fit, snapshot.Photos[0].Mode);
            Assert.Equal(AutoCutPic.Core.Calculators.TargetOrientation.Portrait, snapshot.Photos[0].Orientation);
        }

        [Fact]
        public void SessionManager_SaveAndLoad_ShouldPreserveExactData()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"autocut_test_{Guid.NewGuid():N}.json");
            try
            {
                var data = new ProjectSessionData
                {
                    TargetSizeName = "5寸",
                    FilterMode = "Hide",
                    CardWidth = 180.0,
                    SelectedIndex = 1,
                    Photos = new List<PhotoSessionItem>
                    {
                        new() { FilePath = "img1.jpg", OffsetX = -0.3, OffsetY = 0.4, CropScale = 0.75, Mode = CutMode.Fill },
                        new() { FilePath = "img2.jpg", OffsetX = 0.1, OffsetY = 0.2, CropScale = 0.9, Mode = CutMode.Fit }
                    }
                };

                SessionManager.SaveToFile(data, tempFile);
                Assert.True(File.Exists(tempFile));

                var loaded = SessionManager.LoadFromFile(tempFile);
                Assert.NotNull(loaded);
                Assert.Equal("5寸", loaded.TargetSizeName);
                Assert.Equal("Hide", loaded.FilterMode);
                Assert.Equal(180.0, loaded.CardWidth);
                Assert.Equal(1, loaded.SelectedIndex);
                Assert.Equal(2, loaded.Photos.Count);
                Assert.Equal(0.75, loaded.Photos[0].CropScale);
                Assert.Equal(CutMode.Fit, loaded.Photos[1].Mode);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}

