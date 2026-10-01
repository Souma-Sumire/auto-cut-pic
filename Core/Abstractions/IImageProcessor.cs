using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace AutoCutPic.Core.Abstractions
{
    /// <summary>
    /// 待导出的照片项数据
    /// </summary>
    public readonly record struct PhotoExportItem(
        string FilePath,
        double OffsetX,
        double OffsetY,
        CutMode? Mode = null,
        AutoCutPic.Core.Calculators.TargetOrientation? Orientation = null,
        double CropScale = 1.0
    );

    /// <summary>
    /// 图像处理与导出抽象接口
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// 加载物理图像并纠正 EXIF 方向
        /// </summary>
        MagickImage LoadImage(string path);

        /// <summary>
        /// 执行无损内接裁切或留白扩展处理
        /// </summary>
        MagickImage ProcessImage(
            MagickImage original,
            CropSettings settings,
            double offsetX = 0,
            double offsetY = 0,
            AutoCutPic.Core.Calculators.TargetOrientation? orientation = null,
            double cropScale = 1.0
        );

        /// <summary>
        /// 异步多核并行批量导出
        /// </summary>
        Task<ExportBatchResult> ExportBatchAsync(
            IReadOnlyList<PhotoExportItem> items,
            CropSettings settings,
            string outputFolder,
            IProgress<ExportProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            Func<PhotoExportItem, string?>? cachedFileResolver = null
        );
    }
}
