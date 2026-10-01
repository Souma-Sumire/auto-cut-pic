using System;
using AutoCutPic.Core;
using AutoCutPic.Core.Calculators;
using Xunit;

namespace AutoCutPic.Tests
{
    public class CropGeometryCalculatorTests
    {
        [Fact]
        public void TargetPaper_WhenPhotoIsLandscape_ShouldOrientLandscape()
        {
            var size = PhotoSize.Inch6; // 1795 x 1205
            var paper = CropGeometryCalculator.CalculateTargetPaperDimensions(size, 4000, 3000);

            Assert.Equal(1795, paper.Width);
            Assert.Equal(1205, paper.Height);
            Assert.False(paper.IsPortrait);
        }

        [Fact]
        public void TargetPaper_WhenPhotoIsPortrait_ShouldOrientPortrait()
        {
            var size = PhotoSize.Inch6; // 1795 x 1205
            var paper = CropGeometryCalculator.CalculateTargetPaperDimensions(size, 3000, 4000);

            // 竖向照片应翻转相纸为 1205 x 1795
            Assert.Equal(1205, paper.Width);
            Assert.Equal(1795, paper.Height);
            Assert.True(paper.IsPortrait);
        }

        [Fact]
        public void TargetPaper_WhenPhotoSizeZero_ShouldSafelyFallback()
        {
            var size = PhotoSize.Inch6;
            var paper = CropGeometryCalculator.CalculateTargetPaperDimensions(size, 0, 0);

            Assert.True(paper.Width > 0);
            Assert.True(paper.Height > 0);
        }

        [Fact]
        public void FillCrop_LandscapePhotoWiderThanPaper_ShouldCropLeftAndRight()
        {
            // 照片 16:9 (1600 x 900)
            // 目标纸张 4:3 (800 x 600)
            // targetAR = 800/600 = 1.333
            // photoAR = 1600/900 = 1.777
            // 照片更宽 -> cropH = 900, cropW = 900 * (4/3) = 1200
            // excessW = 1600 - 1200 = 400
            var paper = new PaperDimensions(800, 600);

            // 居中裁切 (offsetX = 0)
            var cropCenter = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 0, 0);
            Assert.Equal(1200, cropCenter.Width);
            Assert.Equal(900, cropCenter.Height);
            Assert.Equal(200, cropCenter.X); // (400 / 2) = 200
            Assert.Equal(0, cropCenter.Y);

            // 贴最左裁切 (offsetX = -0.5)
            var cropLeft = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, -0.5, 0);
            Assert.Equal(0, cropLeft.X);

            // 贴最右裁切 (offsetX = 0.5)
            var cropRight = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 0.5, 0);
            Assert.Equal(400, cropRight.X);

            // 越界值应自动钳位 (offsetX = 2.0)
            var cropClamped = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 2.0, 0);
            Assert.Equal(400, cropClamped.X);
        }

        [Fact]
        public void FillCrop_LandscapePhotoTallerThanPaper_ShouldCropTopAndBottom()
        {
            // 照片 4:3 (1200 x 900)
            // 目标纸张 16:9 (1600 x 900)
            // targetAR = 1.777, photoAR = 1.333
            // 照片更高 -> cropW = 1200, cropH = 1200 / (16/9) = 675
            // excessH = 900 - 675 = 225
            var paper = new PaperDimensions(1600, 900);

            // 贴最顶 (offsetY = -0.5)
            var cropTop = CropGeometryCalculator.CalculateFillCrop(1200, 900, paper, 0, -0.5);
            Assert.Equal(1200, cropTop.Width);
            Assert.Equal(675, cropTop.Height);
            Assert.Equal(0, cropTop.X);
            Assert.Equal(0, cropTop.Y);

            // 贴最底 (offsetY = 0.5)
            var cropBottom = CropGeometryCalculator.CalculateFillCrop(1200, 900, paper, 0, 0.5);
            Assert.Equal(225, cropBottom.Y);
        }

        [Fact]
        public void FitPlacement_ShouldKeepEntirePhotoWithWhiteMargins()
        {
            // 照片 1600 x 900 (16:9)
            // 相纸 1000 x 1000 (1:1)
            // scale = min(1000/1600, 1000/900) = 1000/1600 = 0.625
            // scaledW = 1000, scaledH = 900 * 0.625 = 562.5 -> 563
            // top margin = (1000 - 563) / 2 = 218
            var paper = new PaperDimensions(1000, 1000);
            var fit = CropGeometryCalculator.CalculateFitPlacement(1600, 900, paper);

            Assert.Equal(1000, fit.TargetWidth);
            Assert.Equal(1000, fit.TargetHeight);
            Assert.Equal(1000, fit.ScaledWidth);
            Assert.Equal(563, fit.ScaledHeight);
            Assert.Equal(0, fit.MarginLeft);
            Assert.Equal(218, fit.MarginTop);
        }
    }
}
