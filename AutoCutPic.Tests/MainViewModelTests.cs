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
    }
}
