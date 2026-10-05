using System;
using System.Collections.Generic;
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
    /// 工作区草稿生命周期契约：
    /// 1) 启动只探测不载入；2) 全部导出成功后草稿归档且不再重建；3) 再次编辑后草稿恢复持久化。
    /// </summary>
    public class SessionLifecycleTests : IDisposable
    {
        private const string DraftFileName = "workspace_autosave.json";

        private readonly string _testDir;
        private readonly string _draftPath;

        public SessionLifecycleTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AutoCutPic_SessionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _draftPath = Path.Combine(_testDir, DraftFileName);
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
            using var image = new MagickImage(MagickColors.Orange, 900, 600);
            image.Write(path);
            return path;
        }

        private MainViewModel CreateViewModel() => new(imageProcessor: null, autoSaveFilePath: _draftPath);

        private async Task SeedDraftAsync(params string[] photoPaths)
        {
            var draft = new ProjectSessionData
            {
                TargetSizeName = "6寸",
                FilterMode = "Dim",
                CardWidth = 130.0,
                SelectedIndex = 0,
                Photos = new List<PhotoSessionItem>()
            };

            foreach (string path in photoPaths)
            {
                draft.Photos.Add(new PhotoSessionItem
                {
                    FilePath = path,
                    OffsetX = 0.1,
                    OffsetY = -0.2,
                    CropScale = 0.8,
                    Mode = CutMode.Fit,
                    Orientation = TargetOrientation.Landscape,
                    IsAspectMatched = false
                });
            }

            await SessionManager.SaveToFileAsync(draft, _draftPath);
        }

        [Fact]
        public async Task Startup_ShouldOnlyPeekDraft_AndNeverAutoLoadPhotos()
        {
            string photo = CreatePhoto("peek.jpg");
            await SeedDraftAsync(photo);

            var vm = CreateViewModel();

            // 构造视图模型本身绝不触发任何载入
            Assert.Empty(vm.Photos);
            Assert.False(vm.IsLoading);

            bool found = await vm.RefreshRecoverableDraftAsync();

            Assert.True(found);
            Assert.True(vm.HasRecoverableDraft);
            Assert.Contains("1 张照片", vm.RecoverableDraftText);

            // 关键契约：探测阶段绝不把历史工作区自动灌入 Photos
            Assert.Empty(vm.Photos);
            Assert.True(vm.IsDropOverlayVisible);
        }

        [Fact]
        public async Task RefreshRecoverableDraft_WhenDraftPhotosMissing_ShouldReportNothingRecoverable()
        {
            await SeedDraftAsync(Path.Combine(_testDir, "already_deleted.jpg"));

            var vm = CreateViewModel();
            bool found = await vm.RefreshRecoverableDraftAsync();

            Assert.False(found);
            Assert.False(vm.HasRecoverableDraft);
            Assert.Equal(string.Empty, vm.RecoverableDraftText);
            Assert.Empty(vm.Photos);
        }

        [Fact]
        public async Task RestoreAutoSaveDraftAsync_ShouldLoadPhotosOnlyOnExplicitRequest()
        {
            string a = CreatePhoto("restore_a.jpg");
            string b = CreatePhoto("restore_b.jpg");
            await SeedDraftAsync(a, b);

            var vm = CreateViewModel();
            await vm.RefreshRecoverableDraftAsync();
            Assert.Empty(vm.Photos);

            bool restored = await vm.RestoreAutoSaveDraftAsync();

            Assert.True(restored);
            Assert.Equal(2, vm.Photos.Count);
            Assert.Equal(0.1, vm.Photos[0].OffsetX, 6);
            Assert.Equal(0.8, vm.Photos[0].CropScale, 6);
            Assert.False(vm.HasRecoverableDraft);
            Assert.True(vm.HasUnexportedChanges);
        }

        [Fact]
        public async Task DiscardAutoSaveDraft_ShouldDeleteFileAndClearPrompt()
        {
            string photo = CreatePhoto("discard.jpg");
            await SeedDraftAsync(photo);

            var vm = CreateViewModel();
            await vm.RefreshRecoverableDraftAsync();
            Assert.True(File.Exists(_draftPath));
            Assert.True(vm.HasAutoSaveDraft);

            vm.DiscardAutoSaveDraft();

            Assert.False(File.Exists(_draftPath));
            Assert.False(vm.HasRecoverableDraft);
            Assert.False(vm.HasAutoSaveDraft);
            Assert.Empty(vm.Photos);
        }

        [Fact]
        public async Task ExportAsync_WhenEveryPhotoSucceeds_ShouldArchiveDraftAndNeverRebuildIt()
        {
            string photo = CreatePhoto("export.jpg");
            string outputFolder = Path.Combine(_testDir, "Clipped_Photos");

            var vm = CreateViewModel();
            await vm.LoadFiles(new[] { photo });

            Assert.True(vm.HasUnexportedChanges);
            await vm.SaveAutoSaveSessionAsync();
            Assert.True(File.Exists(_draftPath));

            await vm.ExportAsync();

            Assert.True(File.Exists(Path.Combine(outputFolder, "export_6寸.jpg")));
            Assert.False(vm.HasUnexportedChanges);
            Assert.False(File.Exists(_draftPath));
            Assert.Contains("已全部导出", vm.AutoSaveStatusText);

            // 导出结果只写在导出专属文案里，不得污染全局 StatusText（信息单一归属）；
            // 同时 Progress<T> 的迟到回调不得把结果覆盖回 "正在导出 (N/N, 100%)"
            Assert.Contains("导出完成", vm.ExportStatusText);
            Assert.DoesNotContain("正在导出", vm.ExportStatusText);
            Assert.DoesNotContain("导出完成", vm.StatusText);

            // 关闭程序时的兜底写入同样不得把已交付的工作区重新变成"未完成"
            vm.SaveSessionImmediately();
            Assert.False(File.Exists(_draftPath));

            vm.Dispose();
        }

        [Fact]
        public async Task MarkSessionDirty_AfterFullExport_ShouldResumeDraftPersistence()
        {
            string photo = CreatePhoto("resume.jpg");

            var vm = CreateViewModel();
            await vm.LoadFiles(new[] { photo });
            await vm.ExportAsync();
            Assert.False(File.Exists(_draftPath));

            // 导出后再次编辑 -> 重新产生未交付内容 -> 草稿重新落盘
            vm.MarkSessionDirty();
            Assert.True(vm.HasUnexportedChanges);

            await vm.SaveAutoSaveSessionAsync();
            Assert.True(File.Exists(_draftPath));

            vm.Dispose();
        }

        [Fact]
        public void SessionManager_DraftHelpers_ShouldBePathScopedAndIdempotent()
        {
            string path = Path.Combine(_testDir, "scoped.json");

            Assert.False(SessionManager.HasAutoSaveDraft(path));

            // 幂等：文件不存在时清理不得抛异常
            SessionManager.ClearAutoSaveDraft(path);
            Assert.False(SessionManager.HasAutoSaveDraft(path));

            SessionManager.SaveToFile(new ProjectSessionData { TargetSizeName = "5寸" }, path);

            Assert.True(SessionManager.HasAutoSaveDraft(path));
            Assert.False(SessionManager.HasAutoSaveDraft(_draftPath));

            SessionManager.ClearAutoSaveDraft(path);
            Assert.False(SessionManager.HasAutoSaveDraft(path));
        }
    }
}
