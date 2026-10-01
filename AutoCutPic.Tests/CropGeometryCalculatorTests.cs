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

        [Fact]
        public void FillCrop_ExtremeAspectRatios_ShouldClampAndNotThrow()
        {
            var paper = new PaperDimensions(1795, 1205);

            // 100:1 极端全景宽图
            var cropUltraWide = CropGeometryCalculator.CalculateFillCrop(10000, 100, paper, 0, 0);
            Assert.True(cropUltraWide.Width > 0 && cropUltraWide.Width <= 10000);
            Assert.True(cropUltraWide.Height > 0 && cropUltraWide.Height <= 100);
            Assert.True(cropUltraWide.X >= 0 && cropUltraWide.X + cropUltraWide.Width <= 10000);
            Assert.True(cropUltraWide.Y >= 0 && cropUltraWide.Y + cropUltraWide.Height <= 100);

            // 1:100 极端细长纵图
            var cropUltraTall = CropGeometryCalculator.CalculateFillCrop(100, 10000, paper, 0, 0);
            Assert.True(cropUltraTall.Width > 0 && cropUltraTall.Width <= 100);
            Assert.True(cropUltraTall.Height > 0 && cropUltraTall.Height <= 10000);
            Assert.True(cropUltraTall.X >= 0 && cropUltraTall.X + cropUltraTall.Width <= 100);
            Assert.True(cropUltraTall.Y >= 0 && cropUltraTall.Y + cropUltraTall.Height <= 10000);
        }

        [Theory]
        [InlineData(-100, 500)]
        [InlineData(500, -100)]
        [InlineData(0, 0)]
        public void CalculateFillCrop_WhenInvalidNegativeInputs_ShouldSafelyReturnNonZero(int w, int h)
        {
            var paper = new PaperDimensions(1795, 1205);
            var crop = CropGeometryCalculator.CalculateFillCrop(w, h, paper, 0, 0);

            Assert.True(crop.Width >= 1);
            Assert.True(crop.Height >= 1);
            Assert.True(crop.X >= 0);
            Assert.True(crop.Y >= 0);
        }

        [Theory]
        [InlineData(-0.5, 0)]
        [InlineData(0.5, 1)]
        public void FillCrop_ExactBoundaryOffsets_ShouldAlignCorrectly(double offset, int expectedAlign)
        {
            // 2000 x 1000 照片裁切到 1000 x 1000 相纸 -> cropW = 1000, excessW = 1000
            var paper = new PaperDimensions(1000, 1000);
            var crop = CropGeometryCalculator.CalculateFillCrop(2000, 1000, paper, offset, 0);

            Assert.Equal(1000, crop.Width);
            Assert.Equal(1000, crop.Height);
            if (expectedAlign == 0)
            {
                // 最左端
                Assert.Equal(0, crop.X);
            }
            else
            {
                // 最右端
                Assert.Equal(1000, crop.X);
            }
        }

        [Fact]
        public void CalculateBatchCardLayout_FitMode_ShouldScaleAndCenterWithinPaper()
        {
            var size = PhotoSize.Inch6;
            var layout = CropGeometryCalculator.CalculateBatchCardLayout(
                cardBoxWidth: 200,
                cardBoxHeight: 140,
                photoWidth: 1600,
                photoHeight: 900,
                targetSize: size,
                mode: CutMode.Fit
            );

            Assert.True(layout.IsFit);
            Assert.True(layout.PaperWidth > 0 && layout.PaperWidth <= 200);
            Assert.True(layout.PaperHeight > 0 && layout.PaperHeight <= 140);
            Assert.True(layout.ImageWidth <= layout.PaperWidth);
            Assert.True(layout.ImageHeight <= layout.PaperHeight);
            Assert.True(layout.MarginLeft >= 0);
            Assert.True(layout.MarginTop >= 0);
        }

        [Fact]
        public void CalculateBatchCardLayout_FillMode_ShouldSpanPaperAndClampOffsets()
        {
            var size = PhotoSize.Inch6;
            // 贴最左端
            var layoutLeft = CropGeometryCalculator.CalculateBatchCardLayout(
                cardBoxWidth: 200,
                cardBoxHeight: 140,
                photoWidth: 2000,
                photoHeight: 1000,
                targetSize: size,
                mode: CutMode.Fill,
                offsetX: -0.5,
                offsetY: 0
            );

            Assert.False(layoutLeft.IsFit);
            Assert.True(layoutLeft.PaperWidth > 0);
            Assert.True(layoutLeft.ImageWidth >= layoutLeft.PaperWidth);
            Assert.Equal(0, layoutLeft.MarginLeft, 1);

            // 贴最右端 (offsetX = 0.5) 且 MarginLeft 必须为负数（左移图片露右侧）
            var layoutRight = CropGeometryCalculator.CalculateBatchCardLayout(
                cardBoxWidth: 200,
                cardBoxHeight: 140,
                photoWidth: 2000,
                photoHeight: 1000,
                targetSize: size,
                mode: CutMode.Fill,
                offsetX: 0.5,
                offsetY: 0
            );

            Assert.True(layoutRight.MarginLeft < 0);
        }

        [Fact]
        public void CalculateBatchCardLayout_InvalidInputs_ShouldReturnSafeDefaults()
        {
            var layout = CropGeometryCalculator.CalculateBatchCardLayout(0, 0, 0, 0, PhotoSize.Inch6, CutMode.Fill);
            Assert.True(layout.PaperWidth >= 0);
            Assert.True(layout.PaperHeight >= 0);
        }

        [Fact]
        public void CalculateBatchCardCropLayout_FillMode_ShouldCalculateAccurateCropBoxAndImageBounds()
        {
            var size = PhotoSize.Inch6; // 1795 x 1205
            var layout = CropGeometryCalculator.CalculateBatchCardCropLayout(
                boxWidth: 180,
                boxHeight: 120,
                photoWidth: 1600,
                photoHeight: 800, // 2:1 超宽横图
                targetSize: size,
                mode: CutMode.Fill,
                offsetX: 0,
                offsetY: 0
            );

            Assert.False(layout.IsFit);
            Assert.True(layout.ImageWidth > 0 && layout.ImageWidth <= 180);
            Assert.True(layout.ImageHeight > 0 && layout.ImageHeight <= 120);
            // 裁切框必须在原图内部
            Assert.True(layout.CropLeft >= layout.ImageLeft);
            Assert.True(layout.CropTop >= layout.ImageTop);
            Assert.True(layout.CropWidth <= layout.ImageWidth);
            Assert.True(layout.CropHeight <= layout.ImageHeight);
        }

        [Fact]
        public void CalculateBatchCardCropLayout_FitMode_ShouldSetIsFitTrue()
        {
            var size = PhotoSize.Inch6;
            var layout = CropGeometryCalculator.CalculateBatchCardCropLayout(
                boxWidth: 180,
                boxHeight: 120,
                photoWidth: 1600,
                photoHeight: 800,
                targetSize: size,
                mode: CutMode.Fit
            );

            Assert.True(layout.IsFit);
            Assert.True(layout.CropWidth > 0 && layout.CropHeight > 0);
            Assert.True(layout.ImageWidth <= layout.CropWidth);
            Assert.True(layout.ImageHeight <= layout.CropHeight);
        }

        [Fact]
        public void IsAspectMatched_Standard3to2PhotoWithInch6_ShouldReturnTrue()
        {
            // 标准单反 3:2 照片 (3000 x 2000)，冲印 6寸 (1795 x 1205，比例约 1.4896)
            // 两者相对差异约 0.69% <= 1.5%
            bool isMatched = CropGeometryCalculator.IsAspectMatched(3000, 2000, PhotoSize.Inch6);
            Assert.True(isMatched);

            // 竖向 2:3 照片 (2000 x 3000) 冲印 6寸 (自适应纵向 1205 x 1795)
            bool isPortraitMatched = CropGeometryCalculator.IsAspectMatched(2000, 3000, PhotoSize.Inch6);
            Assert.True(isPortraitMatched);
        }

        [Fact]
        public void IsAspectMatched_NonMatchingRatio_ShouldReturnFalse()
        {
            // 手机常见的 4:3 照片 (4000 x 3000，比例 1.333) 冲印 6寸 (比例 1.490)，相差超过 10%
            bool isMatched43 = CropGeometryCalculator.IsAspectMatched(4000, 3000, PhotoSize.Inch6);
            Assert.False(isMatched43);

            // 正方形 1:1 照片 (2000 x 2000) 冲印 6寸
            bool isMatchedSquare = CropGeometryCalculator.IsAspectMatched(2000, 2000, PhotoSize.Inch6);
            Assert.False(isMatchedSquare);
        }

        [Fact]
        public void CalculateCropLossRatio_StandardCases_ShouldReturnAccurateRatios()
        {
            // 完全同比例图片损失率为 0
            double zeroLoss = CropGeometryCalculator.CalculateCropLossRatio(1795, 1205, PhotoSize.Inch6);
            Assert.Equal(0.0, zeroLoss, 4);

            // 4:3 照片冲印 6寸 (1795 x 1205, AR 约 1.4896)
            // 4:3 照片 AR 约 1.3333 < 1.4896，高度方向超出，计算裁剪损失率 > 0
            double lossRatio = CropGeometryCalculator.CalculateCropLossRatio(4000, 3000, PhotoSize.Inch6);
            Assert.True(lossRatio > 0.05); // 损失超过 5%
        }

        [Fact]
        public void CalculateTargetPaperDimensions_ExplicitOrientation_ShouldRespectSetting()
        {
            var size = PhotoSize.Inch6; // 宽 1795, 高 1205
            int landscapeW = Math.Max(size.PixelWidth, size.PixelHeight);
            int landscapeH = Math.Min(size.PixelWidth, size.PixelHeight);

            // 针对原本为竖向的图片 (1000 x 2000)，若显式指定 TargetOrientation.Landscape，相纸应为横向
            var paperLandscape = CropGeometryCalculator.CalculateTargetPaperDimensions(size, 1000, 2000, TargetOrientation.Landscape);
            Assert.Equal(landscapeW, paperLandscape.Width);
            Assert.Equal(landscapeH, paperLandscape.Height);

            // 针对原本为横向的图片 (2000 x 1000)，若显式指定 TargetOrientation.Portrait，相纸应为纵向
            var paperPortrait = CropGeometryCalculator.CalculateTargetPaperDimensions(size, 2000, 1000, TargetOrientation.Portrait);
            Assert.Equal(landscapeH, paperPortrait.Width);
            Assert.Equal(landscapeW, paperPortrait.Height);
        }

        [Fact]
        public void DetectEffectiveOrientation_ScreenshotWithBlackBars_ShouldDetectLandscapeContent()
        {
            // 模拟手机竖屏截屏：总尺寸 100 x 200 (长宽比 1:2 纵向)
            // 上下各 60 行是纯黑黑边 (y: 0~59 与 140~199)
            // 中间 y: 60~139 为横版图片区域 (高度 80，宽度 100，宽高比 100/80 = 1.25 > 1 横向)
            int width = 100;
            int height = 200;

            var orientation = CropGeometryCalculator.DetectEffectiveOrientation(width, height, (x, y) =>
            {
                if (y < 60 || y >= 140)
                {
                    // 纯黑黑边
                    return ((byte)0, (byte)0, (byte)0);
                }
                // 中间有效彩色内容
                return ((byte)120, (byte)150, (byte)200);
            });

            Assert.Equal(TargetOrientation.Landscape, orientation);
        }

        [Fact]
        public void DetectEffectiveOrientation_ScreenshotWithWhiteBars_ShouldDetectLandscapeContent()
        {
            // 模拟手机竖屏截屏：上下各 50 行为纯白边框 (255, 255, 255)
            // 中间 y: 50~149 为横版有效内容 (100 x 100 为正方形或 120 x 80 横版)
            int width = 120;
            int height = 200;

            var orientation = CropGeometryCalculator.DetectEffectiveOrientation(width, height, (x, y) =>
            {
                if (y < 60 || y >= 140)
                {
                    // 纯白边
                    return ((byte)255, (byte)255, (byte)255);
                }
                return ((byte)100, (byte)100, (byte)100);
            });

            Assert.Equal(TargetOrientation.Landscape, orientation);
        }

        [Fact]
        public void DetectEffectiveOrientation_NormalPortraitPhoto_ShouldReturnPortrait()
        {
            // 普通正常竖版照片 (100 x 200)，无黑白长边矩形边框
            int width = 100;
            int height = 200;

            var orientation = CropGeometryCalculator.DetectEffectiveOrientation(width, height, (x, y) =>
            {
                // 普通风景/人像颜色
                return ((byte)128, (byte)128, (byte)128);
            });

            Assert.Equal(TargetOrientation.Portrait, orientation);
        }

        [Fact]
        public void FillCrop_WithCropScale_ShouldScaleCropBoxAndAllow2DAxisMovement()
        {
            // 照片 1600 x 900，相纸 800 x 600 (4:3)
            // 满版时 (scale=1.0): cropH=900, cropW=1200, excessW=400, excessH=0
            var paper = new PaperDimensions(800, 600);

            // 当缩小裁切尺寸为 0.5 时：
            // baseCropW=1200, baseCropH=900
            // 缩小后: cropW = 1200 * 0.5 = 600, cropH = 900 * 0.5 = 450
            // excessW = 1600 - 600 = 1000, excessH = 900 - 450 = 450
            var cropScaled = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 0, 0, 0.5);
            Assert.Equal(600, cropScaled.Width);
            Assert.Equal(450, cropScaled.Height);
            Assert.Equal(500, cropScaled.X); // (1000 / 2) = 500
            Assert.Equal(225, cropScaled.Y); // (450 / 2) = 225

            // 在缩小裁切尺寸后，Y 轴也具备了平移空间 (offsetY = -0.5 贴顶)
            var cropTop = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 0, -0.5, 0.5);
            Assert.Equal(0, cropTop.Y);

            // 贴底 (offsetY = 0.5)
            var cropBottom = CropGeometryCalculator.CalculateFillCrop(1600, 900, paper, 0, 0.5, 0.5);
            Assert.Equal(450, cropBottom.Y);
        }

        [Fact]
        public void CalculateBatchCardCropLayout_WithCropScale_ShouldScaleCropDimensions()
        {
            var layout1 = CropGeometryCalculator.CalculateBatchCardCropLayout(
                128, 88, 1600, 900, PhotoSize.Inch6, CutMode.Fill, 0, 0, null, 1.0);

            var layoutHalf = CropGeometryCalculator.CalculateBatchCardCropLayout(
                128, 88, 1600, 900, PhotoSize.Inch6, CutMode.Fill, 0, 0, null, 0.5);

            Assert.True(layoutHalf.CropWidth < layout1.CropWidth);
            Assert.True(layoutHalf.CropHeight < layout1.CropHeight);
        }
    }
}


