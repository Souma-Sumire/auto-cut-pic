namespace AutoCutPic.Core.Calculators
{
    /// <summary>
    /// 表示冲印相纸的目标像素物理尺寸（已执行横竖构图自适应）
    /// </summary>
    public readonly record struct PaperDimensions(int Width, int Height)
    {
        public double AspectRatio => Height > 0 ? (double)Width / Height : 1.0;
        public bool IsPortrait => Height > Width;
    }

    /// <summary>
    /// 表示裁切窗口在原图物理坐标系中的矩形范围
    /// </summary>
    public readonly record struct CropRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// 表示照片在目标相纸上的缩放布局（用于留白 Fit 模式）
    /// </summary>
    public readonly record struct FitPlacement(
        int TargetWidth,
        int TargetHeight,
        int ScaledWidth,
        int ScaledHeight,
        int MarginLeft,
        int MarginTop
    );
}
