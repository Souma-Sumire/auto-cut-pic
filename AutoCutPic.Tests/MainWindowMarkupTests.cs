using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace AutoCutPic.Tests
{
    /// <summary>
    /// 界面的"信息单一归属"契约以静态方式固化：解析 MainWindow.xaml 断言每条状态信息
    /// 只有一个渲染位置，避免再次出现"同一句话在状态栏与面板上各写一遍"的回归。
    /// 这类断言是纯文本结构检查，不依赖启动界面或截图。
    /// </summary>
    public class MainWindowMarkupTests
    {
        /// <summary>只允许在一个位置渲染的状态信息（AGENTS.md §3 信息单一归属）。</summary>
        private static readonly string[] SingleOwnerBindings =
        {
            "StatusText",
            "AutoSaveStatusText",
            "PreExportStatusText",
            "OverallProgressText",
            "OverallProgressPercent",
            "ExportStatusText",
            "ExportProgress",
            "LoadingStatusText",
        };

        [Fact]
        public void StatusBindings_ShouldEachRenderExactlyOnce()
        {
            string xaml = ReadMainWindowXaml();

            foreach (string property in SingleOwnerBindings)
            {
                int count = CountBindings(xaml, property);
                Assert.True(
                    count == 1,
                    $"{property} 在 MainWindow.xaml 中渲染了 {count} 处，违反\"信息单一归属\"契约（应为 1 处）");
            }
        }

        [Fact]
        public void LoadingProgress_ShouldOnlyAppearInsideTheModalMask()
        {
            string xaml = ReadMainWindowXaml();

            // 进度条与其百分比读数属于同一个模态控件对，其余位置不得再放一份
            Assert.Equal(2, CountBindings(xaml, "LoadingProgress"));
        }

        [Fact]
        public void GalleryCard_ShouldNotCarryRestatingModeBadge()
        {
            string xaml = ReadMainWindowXaml();

            // 留白模式下白边本身已经可见，卡片上再挂"留白"胶囊属于复述可见状态的无意义提示
            Assert.DoesNotContain("Text=\"留白\"", xaml);
        }

        private static int CountBindings(string xaml, string property)
        {
            return Regex.Matches(xaml, @"\{Binding " + Regex.Escape(property) + @"[,}]").Count;
        }

        private static string ReadMainWindowXaml()
        {
            string? path = FindRepositoryFile("MainWindow.xaml");
            Assert.True(path != null, "未能定位 MainWindow.xaml，标记契约测试必须在仓库工作区内运行");
            return File.ReadAllText(path!);
        }

        private static string? FindRepositoryFile(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
