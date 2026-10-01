using System;
using System.Collections.Generic;

namespace AutoCutPic.Core.Abstractions
{
    /// <summary>
    /// 导出进度报告
    /// </summary>
    public readonly record struct ExportProgressReport(
        int ProcessedCount,
        int TotalCount,
        string CurrentFileName
    )
    {
        public double Percentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100.0 : 0.0;
    }

    /// <summary>
    /// 单个文件的导出错误记录
    /// </summary>
    public readonly record struct ExportFailure(
        string FilePath,
        string ErrorMessage
    );

    /// <summary>
    /// 批量导出的最终结果汇总
    /// </summary>
    public class ExportBatchResult
    {
        public int TotalCount { get; init; }
        public int SuccessCount { get; init; }
        public int FailedCount => Failures.Count;
        public bool IsCancelled { get; init; }
        public IReadOnlyList<ExportFailure> Failures { get; init; } = Array.Empty<ExportFailure>();
    }
}
