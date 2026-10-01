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
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double CropScale { get; set; } = 1.0;
        public CutMode Mode { get; set; } = CutMode.Fill;
        public TargetOrientation Orientation { get; set; } = TargetOrientation.Landscape;
        public bool IsAspectMatched { get; set; }
    }

    public class ProjectSessionData
    {
        public int Version { get; set; } = 1;
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
            catch
            {
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
            catch
            {
                return null;
            }
        }

        public static bool HasAutoSaveDraft()
        {
            return File.Exists(AutoSaveFilePath);
        }

        public static void ClearAutoSaveDraft()
        {
            try
            {
                if (File.Exists(AutoSaveFilePath))
                {
                    File.Delete(AutoSaveFilePath);
                }
            }
            catch { }
        }
    }
}
