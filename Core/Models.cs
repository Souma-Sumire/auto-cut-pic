using System;

namespace AutoCutPic.Core
{
    public record class PhotoSize
    {
        public required string Name { get; set; }
        public double WidthCm { get; set; }
        public double HeightCm { get; set; }
        public int Dpi { get; set; } = 300;

        public int PixelWidth => (int)Math.Round((WidthCm / 2.54) * Dpi, MidpointRounding.AwayFromZero);
        public int PixelHeight => (int)Math.Round((HeightCm / 2.54) * Dpi, MidpointRounding.AwayFromZero);

        public static PhotoSize Inch3 { get; } =
            new()
            {
                Name = "3寸",
                WidthCm = 8.9,
                HeightCm = 6.3,
            };
        public static PhotoSize Inch5 { get; } =
            new()
            {
                Name = "5寸",
                WidthCm = 12.7,
                HeightCm = 8.9,
            };
        public static PhotoSize Inch6 { get; } =
            new()
            {
                Name = "6寸",
                WidthCm = 15.2,
                HeightCm = 10.2,
            };
        public static PhotoSize Inch7 { get; } =
            new()
            {
                Name = "7寸",
                WidthCm = 17.8,
                HeightCm = 12.7,
            };
        public static PhotoSize Inch8 { get; } =
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
        Fit,  // 留白完整
    }

    public record CropSettings
    {
        public required PhotoSize TargetSize { get; set; }
        public CutMode Mode { get; set; }
    }
}
