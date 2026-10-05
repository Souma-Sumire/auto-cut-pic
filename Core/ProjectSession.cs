using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AutoCutPic.Core.Calculators;

namespace AutoCutPic.Core
{
    public class PhotoSessionItem
    {
        public string FilePath { get; set; } = string.Empty;

        /// <summary>裁剪填充（Fill）模式的归一化偏移量。</summary>
        public double OffsetX { get; set; }

        /// <summary>裁剪填充（Fill）模式的归一化偏移量。</summary>
        public double OffsetY { get; set; }

        /// <summary>留白完整（Fit）模式的归一化偏移量；旧版工程无此字段，读取后为 null。</summary>
        public double? OffsetXFit { get; set; }

        /// <summary>留白完整（Fit）模式的归一化偏移量；旧版工程无此字段，读取后为 null。</summary>
        public double? OffsetYFit { get; set; }

        public double CropScale { get; set; } = 1.0;
        public CutMode Mode { get; set; } = CutMode.Fill;
        public TargetOrientation Orientation { get; set; } = TargetOrientation.Landscape;
        public bool IsAspectMatched { get; set; }

        /// <summary>
        /// 解析出两种模式各自的偏移量，兼容旧版工程文件。
        /// <para>
        /// 旧版（无 <see cref="OffsetXFit"/>）只有一份共用偏移量，无法还原两种模式的独立构图：
        /// 此时把该值归还给它所属的模式，另一种模式归零居中 —— 这正好符合
        /// "裁剪填充拖出的取景位置不应影响留白完整排版"的期望。
        /// </para>
        /// </summary>
        public ModeOffsets ResolveOffsets()
        {
            bool isLegacy = OffsetXFit is null || OffsetYFit is null;
            if (!isLegacy)
            {
                return new ModeOffsets(OffsetX, OffsetY, OffsetXFit!.Value, OffsetYFit!.Value);
            }

            return Mode == CutMode.Fit
                ? new ModeOffsets(0, 0, OffsetX, OffsetY)
                : new ModeOffsets(OffsetX, OffsetY, 0, 0);
        }
    }

    /// <summary>两种裁切模式各自持有的归一化构图偏移量。</summary>
    public readonly record struct ModeOffsets(double FillX, double FillY, double FitX, double FitY);

    public class ProjectSessionData
    {
        public int Version { get; set; } = 2;
        public DateTime LastSavedTime { get; set; } = DateTime.UtcNow;
        public string TargetSizeName { get; set; } = "6寸";
        public string FilterMode { get; set; } = "Dim";
        public double CardWidth { get; set; } = 130.0;
        public int SelectedIndex { get; set; } = 0;
        public List<PhotoSessionItem> Photos { get; set; } = new();
    }

    public static class SessionManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string AutoSaveFilePath
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "AutoCutPic");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "workspace_autosave.json");
            }
        }

        public static void SaveToFile(ProjectSessionData data, string filePath)
        {
            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = filePath + ".tmp";
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, filePath, true);
        }

        public static async Task SaveToFileAsync(ProjectSessionData data, string filePath)
        {
            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = filePath + ".tmp";
            string json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, filePath, true);
        }

        public static ProjectSessionData? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ProjectSessionData>(json, JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionManager] 读取会话文件失败: {filePath}, 原因: {ex.Message}");
                return null;
            }
        }

        public static async Task<ProjectSessionData?> LoadFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<ProjectSessionData>(json, JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionManager] 读取会话文件失败: {filePath}, 原因: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断指定路径是否存在可恢复的工作区草稿；<paramref name="filePath"/> 为 null 时使用默认临时目录。
        /// </summary>
        public static bool HasAutoSaveDraft(string? filePath = null)
        {
            return File.Exists(filePath ?? AutoSaveFilePath);
        }

        /// <summary>
        /// 幂等删除工作区草稿（文件不存在时不产生任何副作用）。
        /// </summary>
        public static void ClearAutoSaveDraft(string? filePath = null)
        {
            string target = filePath ?? AutoSaveFilePath;
            try
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionManager] 清理工作区草稿失败: {target}, 原因: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SessionManager] 无权清理工作区草稿: {target}, 原因: {ex.Message}");
            }
        }
    }
}
