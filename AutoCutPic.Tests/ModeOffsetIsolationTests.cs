using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using AutoCutPic.Core;
using AutoCutPic.Core.Calculators;
using AutoCutPic.ViewModels;
using ImageMagick;
using Xunit;

namespace AutoCutPic.Tests
{
    /// <summary>
    /// 裁剪填充（Fill）与留白完整（Fit）的构图偏移量必须各自独立：
    /// 在一种模式里拖出来的位置，绝不能影响另一种模式的排版。
    /// </summary>
    public class ModeOffsetIsolationTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _draftPath;

        public ModeOffsetIsolationTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_ModeOffsetTests_" + Guid.NewGuid().ToString("N"));
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

        private string CreatePhoto(string fileName)
        {
            string path = Path.Combine(_testDir, fileName);
            using var image = new MagickImage(MagickColors.Teal, 900, 600);
            image.Write(path);
            return path;
        }

        [Fact]
        public void PhotoViewModel_ModeOffsets_ShouldBeIndependent()
        {
            var photo = new PhotoViewModel("photo.jpg") { Mode = CutMode.Fill };

            // Fill 模式拖动出的取景位置
            photo.OffsetX = 0.3;
            photo.OffsetY = -0.4;
            Assert.Equal(0.3, photo.FillOffsetX, 6);
            Assert.Equal(-0.4, photo.FillOffsetY, 6);

            // 切到 Fit：不得继承 Fill 的取景位置，必须从居中开始
            photo.Mode = CutMode.Fit;
            Assert.Equal(0.0, photo.OffsetX, 6);
            Assert.Equal(0.0, photo.OffsetY, 6);

            // Fit 自己的排版位置
            photo.OffsetX = -0.2;
            photo.OffsetY = 0.1;
            Assert.Equal(-0.2, photo.FitOffsetX, 6);
            Assert.Equal(0.1, photo.FitOffsetY, 6);

            // 切回 Fill：原构图必须原封不动地恢复
            photo.Mode = CutMode.Fill;
            Assert.Equal(0.3, photo.OffsetX, 6);
            Assert.Equal(-0.4, photo.OffsetY, 6);

            // 反向亦然：Fill 的后续修改不得污染 Fit
            photo.OffsetX = 0.5;
            photo.Mode = CutMode.Fit;
            Assert.Equal(-0.2, photo.OffsetX, 6);
            Assert.Equal(0.5, photo.FillOffsetX, 6);
        }

        [Fact]
        public void PhotoViewModel_ModeSwitch_ShouldNotifyOffsetProperties()
        {
            var photo = new PhotoViewModel("photo.jpg") { Mode = CutMode.Fill };
            photo.OffsetX = 0.25;

            var raised = new List<string?>();
            photo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            photo.Mode = CutMode.Fit;

            // 缺少这两个通知，界面会继续显示上一模式的构图
            Assert.Contains(nameof(PhotoViewModel.OffsetX), raised);
            Assert.Contains(nameof(PhotoViewModel.OffsetY), raised);
        }

        [Fact]
        public void ResetCrop_ShouldClearBothModeOffsets()
        {
            var photo = new PhotoViewModel("photo.jpg");
            photo.ApplyModeOffsets(0.4, -0.3, -0.45, 0.2);
            photo.CropScale = 0.7;

            var vm = new MainViewModel(imageProcessor: null, autoSaveFilePath: _draftPath);
            vm.Photos.Add(photo);
            vm.SelectedPhoto = photo;

            vm.ResetCrop();

            Assert.Equal(0.0, photo.FillOffsetX, 6);
            Assert.Equal(0.0, photo.FillOffsetY, 6);
            Assert.Equal(0.0, photo.FitOffsetX, 6);
            Assert.Equal(0.0, photo.FitOffsetY, 6);
            Assert.Equal(1.0, photo.CropScale, 6);
        }

        [Fact]
        public void PhotoSessionItem_ResolveOffsets_WhenLegacyFile_ShouldAssignOffsetToRecordedMode()
        {
            // 旧版工程只有一份共用偏移量，归属它当时所处的模式，另一模式归零居中
            var legacyFill = new PhotoSessionItem
            {
                FilePath = "a.jpg",
                OffsetX = 0.2,
                OffsetY = 0.1,
                Mode = CutMode.Fill,
                OffsetXFit = null,
                OffsetYFit = null
            };

            var fill = legacyFill.ResolveOffsets();
            Assert.Equal(0.2, fill.FillX, 6);
            Assert.Equal(0.1, fill.FillY, 6);
            Assert.Equal(0.0, fill.FitX, 6);
            Assert.Equal(0.0, fill.FitY, 6);

            var legacyFit = new PhotoSessionItem
            {
                FilePath = "b.jpg",
                OffsetX = -0.3,
                OffsetY = 0.35,
                Mode = CutMode.Fit,
                OffsetXFit = null,
                OffsetYFit = null
            };

            var fit = legacyFit.ResolveOffsets();
            Assert.Equal(0.0, fit.FillX, 6);
            Assert.Equal(0.0, fit.FillY, 6);
            Assert.Equal(-0.3, fit.FitX, 6);
            Assert.Equal(0.35, fit.FitY, 6);
        }

        [Fact]
        public void PhotoSessionItem_ResolveOffsets_WhenCurrentFormat_ShouldKeepBothModes()
        {
            var item = new PhotoSessionItem
            {
                FilePath = "c.jpg",
                OffsetX = 0.4,
                OffsetY = -0.2,
                OffsetXFit = -0.15,
                OffsetYFit = 0.3,
                Mode = CutMode.Fit
            };

            var offsets = item.ResolveOffsets();
            Assert.Equal(0.4, offsets.FillX, 6);
            Assert.Equal(-0.2, offsets.FillY, 6);
            Assert.Equal(-0.15, offsets.FitX, 6);
            Assert.Equal(0.3, offsets.FitY, 6);
        }

        [Fact]
        public async Task SessionRoundTrip_ShouldPreservePerModeOffsets()
        {
            string photo = CreatePhoto("roundtrip.jpg");

            var vm = new MainViewModel(imageProcessor: null, autoSaveFilePath: _draftPath);
            await vm.LoadFiles(new[] { photo });

            var target = vm.Photos[0];
            target.Mode = CutMode.Fill;
            target.OffsetX = 0.35;
            target.OffsetY = -0.15;
            target.Mode = CutMode.Fit;
            target.OffsetX = -0.25;
            target.OffsetY = 0.05;

            var snapshot = vm.CreateSessionSnapshot();
            Assert.Equal(0.35, snapshot.Photos[0].OffsetX, 6);
            Assert.Equal(-0.15, snapshot.Photos[0].OffsetY, 6);
            Assert.Equal(-0.25, snapshot.Photos[0].OffsetXFit!.Value, 6);
            Assert.Equal(0.05, snapshot.Photos[0].OffsetYFit!.Value, 6);

            // 经过一次真实落盘/读取后仍然保持两模式的独立构图
            await SessionManager.SaveToFileAsync(snapshot, _draftPath);
            var reloaded = SessionManager.LoadFromFile(_draftPath);
            Assert.NotNull(reloaded);

            var restored = await vm.RestoreSessionAsync(reloaded!);
            Assert.True(restored);

            var result = vm.Photos[0];
            Assert.Equal(CutMode.Fit, result.Mode);
            Assert.Equal(-0.25, result.FitOffsetX, 6);
            Assert.Equal(0.05, result.FitOffsetY, 6);
            Assert.Equal(0.35, result.FillOffsetX, 6);
            Assert.Equal(-0.15, result.FillOffsetY, 6);

            // 切回 Fill 仍能取回自己的构图
            result.Mode = CutMode.Fill;
            Assert.Equal(0.35, result.OffsetX, 6);
            Assert.Equal(-0.15, result.OffsetY, 6);

            vm.Dispose();
        }
    }
}
