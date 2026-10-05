using System;
using System.IO;
using AutoCutPic.ViewModels;
using Xunit;

namespace AutoCutPic.Tests
{
    /// <summary>
    /// 相邻照片切换契约：到达首/尾必须提示用户，严禁绕回另一端。
    /// </summary>
    public class PhotoNavigationTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _draftPath;

        public PhotoNavigationTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_NavTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _draftPath = Path.Combine(_testDir, "workspace_autosave.json");
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

        private MainViewModel CreateViewModel() => new(imageProcessor: null, autoSaveFilePath: _draftPath);

        private static PhotoViewModel Photo(string name, bool matched) =>
            new(name) { IsAspectMatched = matched };

        [Fact]
        public void NavigateRelative_AtLastPhoto_ShouldReportReachedLastAndNotWrapToFirst()
        {
            var vm = CreateViewModel();
            var p1 = Photo("1.jpg", false);
            var p2 = Photo("2.jpg", false);
            vm.Photos.Add(p1);
            vm.Photos.Add(p2);
            vm.SelectedPhoto = p2;
            string statusBefore = vm.StatusText;

            var status = vm.NavigateRelative(1);

            Assert.Equal(PhotoNavigationStatus.ReachedLast, status);
            Assert.Same(p2, vm.SelectedPhoto); // 绝不绕回第一张

            // 边界提示只由视图层的一次性浮层呈现，不得同时写进全局 StatusText，
            // 否则同一条信息会在浮层与底部状态栏上重复出现
            Assert.Equal(statusBefore, vm.StatusText);
            Assert.Equal("已经是最后一张照片了", MainViewModel.DescribeNavigationStatus(status));
        }

        [Fact]
        public void NavigateRelative_AtFirstPhoto_ShouldReportReachedFirstAndNotWrapToLast()
        {
            var vm = CreateViewModel();
            var p1 = Photo("1.jpg", false);
            var p2 = Photo("2.jpg", false);
            vm.Photos.Add(p1);
            vm.Photos.Add(p2);
            vm.SelectedPhoto = p1;
            string statusBefore = vm.StatusText;

            var status = vm.NavigateRelative(-1);

            Assert.Equal(PhotoNavigationStatus.ReachedFirst, status);
            Assert.Same(p1, vm.SelectedPhoto);
            Assert.Equal(statusBefore, vm.StatusText);
            Assert.Equal("已经是第一张照片了", MainViewModel.DescribeNavigationStatus(status));
        }

        [Fact]
        public void NavigateRelative_InMiddle_ShouldMoveWithoutWritingBoundaryHint()
        {
            var vm = CreateViewModel();
            var p1 = Photo("1.jpg", false);
            var p2 = Photo("2.jpg", false);
            var p3 = Photo("3.jpg", false);
            vm.Photos.Add(p1);
            vm.Photos.Add(p2);
            vm.Photos.Add(p3);
            vm.SelectedPhoto = p1;
            vm.StatusText = "保持不变";

            Assert.Equal(PhotoNavigationStatus.Moved, vm.NavigateRelative(1));
            Assert.Same(p2, vm.SelectedPhoto);
            Assert.Equal("保持不变", vm.StatusText);

            Assert.Equal(PhotoNavigationStatus.Moved, vm.NavigateRelative(-1));
            Assert.Same(p1, vm.SelectedPhoto);
        }

        [Fact]
        public void NavigateRelative_SkipMatchedMode_WhenOnlyMatchedRemainAhead_ShouldStopWithHint()
        {
            var vm = CreateViewModel();
            var current = Photo("1.jpg", false);
            var matched1 = Photo("2.jpg", true);
            var matched2 = Photo("3.jpg", true);
            var actionable = Photo("4.jpg", false);
            vm.Photos.Add(current);
            vm.Photos.Add(matched1);
            vm.Photos.Add(matched2);
            vm.Photos.Add(actionable);
            vm.SelectedPhoto = current;

            // 默认淡化免修模式：向前跳过免修照片，直达第 4 张
            Assert.Equal(PhotoNavigationStatus.Moved, vm.NavigateRelative(1));
            Assert.Same(actionable, vm.SelectedPhoto);
        }

        [Fact]
        public void NavigateRelative_SkipMatchedMode_WhenTailIsAllMatched_ShouldNotWrapAround()
        {
            var vm = CreateViewModel();
            var actionable = Photo("1.jpg", false);
            var matched1 = Photo("2.jpg", true);
            var matched2 = Photo("3.jpg", true);
            vm.Photos.Add(actionable);
            vm.Photos.Add(matched1);
            vm.Photos.Add(matched2);
            vm.SelectedPhoto = actionable;

            var status = vm.NavigateRelative(1);

            // 尾部只剩免修照片：停在原地并说明原因，而不是绕回去
            Assert.Equal(PhotoNavigationStatus.NoActionableInDirection, status);
            Assert.Same(actionable, vm.SelectedPhoto);
            Assert.Equal("该方向已经没有需要调整的照片了", MainViewModel.DescribeNavigationStatus(status));
        }

        [Fact]
        public void NavigateRelative_WhenNoPhotoSelected_ShouldSelectFirst()
        {
            var vm = CreateViewModel();
            var p1 = Photo("1.jpg", false);
            vm.Photos.Add(p1);

            Assert.Equal(PhotoNavigationStatus.Moved, vm.NavigateRelative(1));
            Assert.Same(p1, vm.SelectedPhoto);
        }

        [Fact]
        public void NavigateRelative_WhenEmpty_ShouldReportEmpty()
        {
            var vm = CreateViewModel();

            Assert.Equal(PhotoNavigationStatus.Empty, vm.NavigateRelative(1));
            Assert.Equal(PhotoNavigationStatus.Empty, vm.NavigateRelative(-1));
            Assert.Equal("还没有导入任何照片", MainViewModel.DescribeNavigationStatus(PhotoNavigationStatus.Empty));
        }

        [Fact]
        public void DescribeNavigationStatus_WhenMoved_ShouldReturnNoHint()
        {
            Assert.Equal(string.Empty, MainViewModel.DescribeNavigationStatus(PhotoNavigationStatus.Moved));
        }
    }
}
