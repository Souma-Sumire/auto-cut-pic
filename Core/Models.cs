using System;
using System.IO;
using ImageMagick;

namespace AutoCutPic.Core
{
    public class PhotoSize
    {
        public required string Name { get; set; }
        public double WidthCm { get; set; }
        public double HeightCm { get; set; }
        public int Dpi { get; set; } = 300;

        public int PixelWidth => (int)Math.Round((WidthCm / 2.54) * Dpi);
        public int PixelHeight => (int)Math.Round((HeightCm / 2.54) * Dpi);

        public static PhotoSize Inch3 =>
            new()
            {
                Name = "3寸",
                WidthCm = 8.9,
                HeightCm = 6.3,
            };
        public static PhotoSize Inch5 =>
            new()
            {
                Name = "5寸",
                WidthCm = 12.7,
                HeightCm = 8.9,
            };
        public static PhotoSize Inch6 =>
            new()
            {
                Name = "6寸",
                WidthCm = 15.2,
                HeightCm = 10.2,
            };
        public static PhotoSize Inch7 =>
            new()
            {
                Name = "7寸",
                WidthCm = 17.8,
                HeightCm = 12.7,
            };
        public static PhotoSize Inch8 =>
            new()
            {
                Name = "8寸",
                WidthCm = 20.3,
                HeightCm = 15.2,
            };
    }

    public enum CutMode
    {
        Fill, // 裁切填满
        Fit, // 留白完整
    }

    public class PhotoItem
    {
        public required string FilePath { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public MagickImage? Thumbnail { get; set; }
    }

    public class CropSettings
    {
        public required PhotoSize TargetSize { get; set; }
        public CutMode Mode { get; set; }
        public bool IsPortrait { get; set; } // 是否强制竖版

        public CropSettings Clone() => (CropSettings)MemberwiseClone();
    }
}
