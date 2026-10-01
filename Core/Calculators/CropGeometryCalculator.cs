using System;

namespace AutoCutPic.Core.Calculators
{
    /// <summary>
    /// 目标相纸裁切方向
    /// </summary>
    public enum TargetOrientation
    {
        Landscape, // 横向构图 (宽 >= 高)
        Portrait   // 纵向构图 (高 > 宽)
    }

    /// <summary>
    /// 核心照片几何与冲印相纸裁切计算器（无外部依赖纯算法类）
    /// </summary>
    public static class CropGeometryCalculator
    {
        /// <summary>
        /// 根据照片物理宽高及指定方向，自适应计算目标冲印相纸像素尺寸
        /// </summary>
        public static PaperDimensions CalculateTargetPaperDimensions(
            PhotoSize targetSize,
            int photoWidth,
            int photoHeight,
            TargetOrientation? orientation = null)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
            {
                return new PaperDimensions(targetSize?.PixelWidth ?? 1800, targetSize?.PixelHeight ?? 1200);
            }

            int baseW = Math.Max(targetSize.PixelWidth, targetSize.PixelHeight);
            int baseH = Math.Min(targetSize.PixelWidth, targetSize.PixelHeight);

            TargetOrientation effectiveOrientation = orientation ?? (photoHeight > photoWidth ? TargetOrientation.Portrait : TargetOrientation.Landscape);

            return effectiveOrientation == TargetOrientation.Portrait
                ? new PaperDimensions(baseH, baseW)
                : new PaperDimensions(baseW, baseH);
        }

        /// <summary>
        /// 检测有效内容方向：剔除靠近长边边缘的连续纯黑与纯白矩形条（自动识别手机相册截屏等包含横版照片的竖版图片）
        /// </summary>
        public static TargetOrientation DetectEffectiveOrientation(
            int width,
            int height,
            Func<int, int, (byte R, byte G, byte B)> getPixel)
        {
            if (width <= 0 || height <= 0 || getPixel == null)
                return TargetOrientation.Landscape;

            bool isPortrait = height > width;

            static bool IsBorderColor(byte r, byte g, byte b)
            {
                bool isBlack = r <= 30 && g <= 30 && b <= 30;
                bool isWhite = r >= 230 && g >= 230 && b >= 230;
                return isBlack || isWhite;
            }

            if (isPortrait)
            {
                // 长边为纵向 (H > W)，检查顶部和底部连续纯黑/纯白条 (letterbox)
                int sampleStepX = Math.Max(1, width / 20);
                int maxScanH = (int)(height * 0.42);
                const double threshold = 0.82;

                int top = 0;
                int missTop = 0;
                for (int y = 0; y < maxScanH; y++)
                {
                    int bc = 0, total = 0;
                    for (int x = 0; x < width; x += sampleStepX) { var (r, g, b) = getPixel(x, y); if (IsBorderColor(r, g, b)) bc++; total++; }
                    if (total > 0 && (double)bc / total >= threshold) { top = y + 1; missTop = 0; }
                    else if (++missTop > 2) break;
                }

                int bottom = height - 1;
                int missBot = 0;
                for (int y = height - 1; y >= height - maxScanH; y--)
                {
                    int bc = 0, total = 0;
                    for (int x = 0; x < width; x += sampleStepX) { var (r, g, b) = getPixel(x, y); if (IsBorderColor(r, g, b)) bc++; total++; }
                    if (total > 0 && (double)bc / total >= threshold) { bottom = y - 1; missBot = 0; }
                    else if (++missBot > 2) break;
                }

                int effectiveH = Math.Max(1, bottom - top + 1);
                int effectiveW = width;

                if (effectiveW > effectiveH)
                    return TargetOrientation.Landscape;
                return TargetOrientation.Portrait;
            }
            else
            {
                // 长边为横向 (W >= H)，检查左右两侧连续纯黑/纯白条 (pillarbox)
                int sampleStepY = Math.Max(1, height / 20);
                int maxScanW = (int)(width * 0.42);
                const double threshold = 0.82;

                int left = 0;
                int missLeft = 0;
                for (int x = 0; x < maxScanW; x++)
                {
                    int bc = 0, total = 0;
                    for (int y = 0; y < height; y += sampleStepY) { var (r, g, b) = getPixel(x, y); if (IsBorderColor(r, g, b)) bc++; total++; }
                    if (total > 0 && (double)bc / total >= threshold) { left = x + 1; missLeft = 0; }
                    else if (++missLeft > 2) break;
                }

                int right = width - 1;
                int missRight = 0;
                for (int x = width - 1; x >= width - maxScanW; x--)
                {
                    int bc = 0, total = 0;
                    for (int y = 0; y < height; y += sampleStepY) { var (r, g, b) = getPixel(x, y); if (IsBorderColor(r, g, b)) bc++; total++; }
                    if (total > 0 && (double)bc / total >= threshold) { right = x - 1; missRight = 0; }
                    else if (++missRight > 2) break;
                }

                int effectiveW = Math.Max(1, right - left + 1);
                int effectiveH = height;

                if (effectiveH > effectiveW)
                    return TargetOrientation.Portrait;
                return TargetOrientation.Landscape;
            }
        }

        /// <summary>
        /// 检测有效内容方向（基于 RGBA 字节数组）
        /// </summary>
        public static TargetOrientation DetectEffectiveOrientation(
            byte[] rgbOrRgbaBytes,
            int width,
            int height,
            int bytesPerPixel = 4)
        {
            if (rgbOrRgbaBytes == null || width <= 0 || height <= 0)
                return TargetOrientation.Landscape;

            return DetectEffectiveOrientation(width, height, (x, y) =>
            {
                int index = (y * width + x) * bytesPerPixel;
                if (index + 2 < rgbOrRgbaBytes.Length)
                {
                    return (rgbOrRgbaBytes[index], rgbOrRgbaBytes[index + 1], rgbOrRgbaBytes[index + 2]);
                }
                return ((byte)128, (byte)128, (byte)128);
            });
        }

        /// <summary>
        /// 计算裁切填满（Fill）模式下，原图物理坐标系中的裁切窗口
        /// </summary>
        public static CropRect CalculateFillCrop(
            int photoWidth,
            int photoHeight,
            PaperDimensions targetPaper,
            double offsetX = 0,
            double offsetY = 0,
            double cropScale = 1.0)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetPaper.Width <= 0 || targetPaper.Height <= 0)
            {
                return new CropRect(0, 0, Math.Max(1, photoWidth), Math.Max(1, photoHeight));
            }

            double scale = Math.Clamp(cropScale, 0.1, 1.0);
            double targetAR = (double)targetPaper.Width / targetPaper.Height;
            double photoAR = (double)photoWidth / photoHeight;

            int baseCropW;
            int baseCropH;

            if (photoAR > targetAR)
            {
                baseCropH = photoHeight;
                baseCropW = (int)Math.Round(baseCropH * targetAR, MidpointRounding.AwayFromZero);
                baseCropW = Math.Clamp(baseCropW, 1, photoWidth);
            }
            else
            {
                baseCropW = photoWidth;
                baseCropH = (int)Math.Round(baseCropW / targetAR, MidpointRounding.AwayFromZero);
                baseCropH = Math.Clamp(baseCropH, 1, photoHeight);
            }

            int cropW = Math.Max(1, (int)Math.Round(baseCropW * scale, MidpointRounding.AwayFromZero));
            int cropH = Math.Max(1, (int)Math.Round(baseCropH * scale, MidpointRounding.AwayFromZero));

            cropW = Math.Clamp(cropW, 1, photoWidth);
            cropH = Math.Clamp(cropH, 1, photoHeight);

            int excessW = photoWidth - cropW;
            int excessH = photoHeight - cropH;

            double clampedOffsetX = Math.Clamp(offsetX, -0.5, 0.5);
            double clampedOffsetY = Math.Clamp(offsetY, -0.5, 0.5);

            int cropX = (int)Math.Round((excessW / 2.0) + (clampedOffsetX * excessW), MidpointRounding.AwayFromZero);
            int cropY = (int)Math.Round((excessH / 2.0) + (clampedOffsetY * excessH), MidpointRounding.AwayFromZero);

            cropX = Math.Clamp(cropX, 0, Math.Max(0, photoWidth - cropW));
            cropY = Math.Clamp(cropY, 0, Math.Max(0, photoHeight - cropH));

            return new CropRect(cropX, cropY, cropW, cropH);
        }

        /// <summary>
        /// 计算留白完整（Fit）模式下，照片在目标相纸上的等比缩放与居中排版位置
        /// </summary>
        public static FitPlacement CalculateFitPlacement(
            int photoWidth,
            int photoHeight,
            PaperDimensions targetPaper)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetPaper.Width <= 0 || targetPaper.Height <= 0)
            {
                return new FitPlacement(
                    targetPaper.Width,
                    targetPaper.Height,
                    Math.Max(1, photoWidth),
                    Math.Max(1, photoHeight),
                    0,
                    0
                );
            }

            double ratioW = (double)targetPaper.Width / photoWidth;
            double ratioH = (double)targetPaper.Height / photoHeight;
            double scale = Math.Min(ratioW, ratioH);

            int scaledW = Math.Max(1, (int)Math.Round(photoWidth * scale, MidpointRounding.AwayFromZero));
            int scaledH = Math.Max(1, (int)Math.Round(photoHeight * scale, MidpointRounding.AwayFromZero));

            int marginLeft = Math.Max(0, (targetPaper.Width - scaledW) / 2);
            int marginTop = Math.Max(0, (targetPaper.Height - scaledH) / 2);

            return new FitPlacement(
                targetPaper.Width,
                targetPaper.Height,
                scaledW,
                scaledH,
                marginLeft,
                marginTop
            );
        }

        /// <summary>
        /// 计算批量预览网格卡片中相纸视口大小与照片裁切平移布局
        /// </summary>
        public static BatchCardLayout CalculateBatchCardLayout(
            double cardBoxWidth,
            double cardBoxHeight,
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize,
            CutMode mode,
            double offsetX = 0,
            double offsetY = 0,
            TargetOrientation? orientation = null)
        {
            if (cardBoxWidth <= 10 || cardBoxHeight <= 10 || photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
            {
                return new BatchCardLayout(cardBoxWidth, cardBoxHeight, cardBoxWidth, cardBoxHeight, 0, 0, false);
            }

            var targetPaper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight, orientation);
            double targetAR = targetPaper.AspectRatio;
            double photoAR = (double)photoWidth / photoHeight;

            double paperScale = Math.Min(cardBoxWidth / targetPaper.Width, cardBoxHeight / targetPaper.Height);
            double paperW = Math.Max(10, Math.Round(targetPaper.Width * paperScale));
            double paperH = Math.Max(10, Math.Round(targetPaper.Height * paperScale));

            if (mode == CutMode.Fit)
            {
                double imgScale = Math.Min(paperW / photoWidth, paperH / photoHeight);
                double imgW = Math.Max(5, Math.Round(photoWidth * imgScale));
                double imgH = Math.Max(5, Math.Round(photoHeight * imgScale));
                double left = (paperW - imgW) / 2.0;
                double top = (paperH - imgH) / 2.0;

                return new BatchCardLayout(paperW, paperH, imgW, imgH, left, top, true);
            }
            else
            {
                double imgW;
                double imgH;
                double left;
                double top;

                double clampedOffsetX = Math.Clamp(offsetX, -0.5, 0.5);
                double clampedOffsetY = Math.Clamp(offsetY, -0.5, 0.5);

                if (photoAR > targetAR)
                {
                    imgH = paperH;
                    imgW = Math.Round(paperH * photoAR);
                    double excessW = imgW - paperW;
                    left = -((excessW / 2.0) + (clampedOffsetX * excessW));
                    top = 0;
                }
                else
                {
                    imgW = paperW;
                    imgH = Math.Round(paperW / photoAR);
                    double excessH = imgH - paperH;
                    left = 0;
                    top = -((excessH / 2.0) + (clampedOffsetY * excessH));
                }

                return new BatchCardLayout(paperW, paperH, imgW, imgH, left, top, false);
            }
        }

        /// <summary>
        /// 计算批量预览网格卡片中完整原图显示与冲印相纸裁切取景框的几何位置
        /// （基于自身包围盒的相对坐标，确保在任何长宽比下严格居中，绝不歪斜）
        /// </summary>
        public static BatchCardCropLayout CalculateBatchCardCropLayout(
            double boxWidth,
            double boxHeight,
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize,
            CutMode mode,
            double offsetX = 0,
            double offsetY = 0,
            TargetOrientation? orientation = null,
            double cropScale = 1.0)
        {
            if (boxWidth <= 10 || boxHeight <= 10 || photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
            {
                return new BatchCardCropLayout(boxWidth, boxHeight, boxWidth, boxHeight, 0, 0, 0, 0, boxWidth, boxHeight, false);
            }

            var targetPaper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight, orientation);

            if (mode == CutMode.Fit)
            {
                // Fit 留白模式：相纸在视口内自适应，照片在相纸内部居中留白
                double paperScale = Math.Min(boxWidth / targetPaper.Width, boxHeight / targetPaper.Height);
                double paperW = Math.Max(10, Math.Round(targetPaper.Width * paperScale));
                double paperH = Math.Max(10, Math.Round(targetPaper.Height * paperScale));

                double imgScale = Math.Min(paperW / photoWidth, paperH / photoHeight);
                double imgW = Math.Max(5, Math.Round(photoWidth * imgScale));
                double imgH = Math.Max(5, Math.Round(photoHeight * imgScale));
                double imgLeft = Math.Round((paperW - imgW) / 2.0);
                double imgTop = Math.Round((paperH - imgH) / 2.0);

                return new BatchCardCropLayout(
                    paperW, paperH,
                    imgW, imgH, imgLeft, imgTop,
                    0, 0, paperW, paperH,
                    true
                );
            }
            else
            {
                // Fill 填充模式：原图完整展示在 box 内部，在其上精确标出裁切框
                double scale = Math.Min(boxWidth / photoWidth, boxHeight / photoHeight);
                double imgW = Math.Max(10, Math.Round(photoWidth * scale));
                double imgH = Math.Max(10, Math.Round(photoHeight * scale));

                var cropRect = CalculateFillCrop(photoWidth, photoHeight, targetPaper, offsetX, offsetY, cropScale);

                double cropW = Math.Max(5, Math.Round(cropRect.Width * scale));
                double cropH = Math.Max(5, Math.Round(cropRect.Height * scale));
                double cropLeft = Math.Round(cropRect.X * scale);
                double cropTop = Math.Round(cropRect.Y * scale);

                return new BatchCardCropLayout(
                    imgW, imgH,
                    imgW, imgH, 0, 0,
                    cropLeft, cropTop, cropW, cropH,
                    false
                );
            }
        }

        /// <summary>
        /// 判断照片在目标冲印相纸下是否属于同比例或极度相似（裁切损失率低于阈值，默认1.5%）
        /// </summary>
        public static bool IsAspectMatched(
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize,
            TargetOrientation? orientation = null,
            double tolerance = 0.015)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
                return false;

            var targetPaper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight, orientation);
            double lossRatio = CalculateCropLossRatio(photoWidth, photoHeight, targetPaper);
            return lossRatio <= tolerance;
        }

        /// <summary>
        /// 计算在填充（Fill）模式下，原图被裁剪抛弃的面积占原图总面积的比例
        /// </summary>
        public static double CalculateCropLossRatio(
            int photoWidth,
            int photoHeight,
            PaperDimensions targetPaper)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetPaper.Width <= 0 || targetPaper.Height <= 0)
                return 0.0;

            double targetAR = (double)targetPaper.Width / targetPaper.Height;
            double photoAR = (double)photoWidth / photoHeight;

            if (photoAR > targetAR)
            {
                // 横向裁剪：宽度超出，裁剪后实际保留宽度为 photoHeight * targetAR
                double visibleW = photoHeight * targetAR;
                return Math.Max(0.0, 1.0 - (visibleW / photoWidth));
            }
            else
            {
                // 纵向裁剪：高度超出，裁剪后实际保留高度为 photoWidth / targetAR
                double visibleH = photoWidth / targetAR;
                return Math.Max(0.0, 1.0 - (visibleH / photoHeight));
            }
        }

        public static double CalculateCropLossRatio(
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
                return 0.0;

            var paper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight);
            return CalculateCropLossRatio(photoWidth, photoHeight, paper);
        }
    }
}

