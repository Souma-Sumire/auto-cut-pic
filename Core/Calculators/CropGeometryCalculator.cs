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

        /// <summary>判定为纯黑边框的通道亮度上限（刻意收紧，避免把暗部画面当成黑边）。</summary>
        private const int BorderBlackMax = 16;

        /// <summary>判定为纯白边框的通道亮度下限（刻意收紧，避免把高光当成白边）。</summary>
        private const int BorderWhiteMin = 240;

        /// <summary>边框像素必须是中性灰，R/G/B 极差超过该值即视为彩色内容。</summary>
        private const int BorderNeutralTolerance = 10;

        /// <summary>同一条边线内亮度极差上限：真实边框是同一块纯色，画面暗部/高光仍有起伏。</summary>
        private const int BorderLineFlatnessTolerance = 8;

        /// <summary>边框条占长边比例上限，避免整幅偏暗画面被整体判成边框。</summary>
        private const double BorderMaxLengthRatio = 0.42;

        /// <summary>对称容差：两侧厚度差超过较薄一侧的该比例即视为不对称，不予排除。</summary>
        private const double BorderSymmetryToleranceRatio = 0.25;

        private enum BorderTone
        {
            None,
            Black,
            White
        }

        /// <summary>
        /// 检测有效内容方向：识别对称的纯黑/纯白上下（或左右）边框条，从而还原被 letterbox
        /// 包裹的真实构图方向（例如手机相册里横版照片被存成带上下黑边的竖版图）。
        /// <para>
        /// 判定三重门禁，任何一项不满足都不排除，宁可漏判也不误判：
        /// 1. 中性且足够极端（黑 ≤ <see cref="BorderBlackMax"/> / 白 ≥ <see cref="BorderWhiteMin"/>）；
        /// 2. 整条边线是纯色块（亮度极差 ≤ <see cref="BorderLineFlatnessTolerance"/>）；
        /// 3. <b>两侧必须成对且厚度接近</b>（见 <see cref="ResolveSymmetricBorder"/>）。
        /// </para>
        /// </summary>
        public static TargetOrientation DetectEffectiveOrientation(
            int width,
            int height,
            Func<int, int, (byte R, byte G, byte B)> getPixel)
        {
            if (width <= 0 || height <= 0 || getPixel == null)
                return TargetOrientation.Landscape;

            if (height > width)
            {
                // 长边为纵向：检查上下两条边框
                int maxScan = Math.Max(1, (int)(height * BorderMaxLengthRatio));
                int sampleStepX = Math.Max(1, width / 24);

                int runTop = MeasureBorderRun(maxScan, y =>
                    ClassifyBorderLine(width, sampleStepX, x => getPixel(x, y)));

                int runBottom = MeasureBorderRun(maxScan, offset =>
                    ClassifyBorderLine(width, sampleStepX, x => getPixel(x, height - 1 - offset)));

                int strip = ResolveSymmetricBorder(runTop, runBottom);
                int effectiveH = Math.Max(1, height - (strip * 2));

                return width > effectiveH ? TargetOrientation.Landscape : TargetOrientation.Portrait;
            }
            else
            {
                // 长边为横向：检查左右两条边框
                int maxScan = Math.Max(1, (int)(width * BorderMaxLengthRatio));
                int sampleStepY = Math.Max(1, height / 24);

                int runLeft = MeasureBorderRun(maxScan, x =>
                    ClassifyBorderLine(height, sampleStepY, y => getPixel(x, y)));

                int runRight = MeasureBorderRun(maxScan, offset =>
                    ClassifyBorderLine(height, sampleStepY, y => getPixel(width - 1 - offset, y)));

                int strip = ResolveSymmetricBorder(runLeft, runRight);
                int effectiveW = Math.Max(1, width - (strip * 2));

                return height > effectiveW ? TargetOrientation.Portrait : TargetOrientation.Landscape;
            }
        }

        /// <summary>从起始边向内统计连续边框线的数量，遇到内容线或黑白混杂立即停止。</summary>
        private static int MeasureBorderRun(int maxScan, Func<int, BorderTone> classifyOffset)
        {
            BorderTone tone = BorderTone.None;
            int count = 0;

            for (int offset = 0; offset < maxScan; offset++)
            {
                BorderTone current = classifyOffset(offset);
                if (current == BorderTone.None)
                {
                    break;
                }

                if (tone == BorderTone.None)
                {
                    tone = current;
                }
                else if (tone != current)
                {
                    break; // 黑边与白边不得混在同一条边框里
                }

                count++;
            }

            return count;
        }

        /// <summary>把一整条边线判定为纯黑边、纯白边或内容；只要有一个采样点像内容就整条否定。</summary>
        private static BorderTone ClassifyBorderLine(
            int lineLength,
            int sampleStep,
            Func<int, (byte R, byte G, byte B)> sampleAt)
        {
            BorderTone tone = BorderTone.None;
            int lumaMin = 255;
            int lumaMax = 0;

            for (int pos = 0; pos < lineLength; pos += sampleStep)
            {
                var (r, g, b) = sampleAt(pos);
                BorderTone pixelTone = ClassifyBorderPixel(r, g, b);
                if (pixelTone == BorderTone.None)
                {
                    return BorderTone.None;
                }

                if (tone == BorderTone.None)
                {
                    tone = pixelTone;
                }
                else if (tone != pixelTone)
                {
                    return BorderTone.None;
                }

                int luma = (r + g + b) / 3;
                if (luma < lumaMin) lumaMin = luma;
                if (luma > lumaMax) lumaMax = luma;
            }

            return lumaMax - lumaMin > BorderLineFlatnessTolerance ? BorderTone.None : tone;
        }

        private static BorderTone ClassifyBorderPixel(byte r, byte g, byte b)
        {
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));

            if (max - min > BorderNeutralTolerance)
            {
                return BorderTone.None; // 有彩色，属于画面内容
            }

            if (max <= BorderBlackMax)
            {
                return BorderTone.Black;
            }

            if (min >= BorderWhiteMin)
            {
                return BorderTone.White;
            }

            return BorderTone.None;
        }

        /// <summary>
        /// 两侧边框必须成对存在且厚度接近才允许排除，返回可安全裁掉的单侧厚度（0 表示不排除）。
        /// 只有一侧有边框、或两侧厚度差异超过较薄一侧的
        /// <see cref="BorderSymmetryToleranceRatio"/> 时，都判定为画面内容而非成像边框。
        /// </summary>
        public static int ResolveSymmetricBorder(int runA, int runB)
        {
            if (runA <= 0 || runB <= 0)
            {
                return 0; // 单侧边框不是 letterbox，排除它会直接切掉真实画面
            }

            int thinner = Math.Min(runA, runB);
            int thicker = Math.Max(runA, runB);
            int tolerance = Math.Max(2, (int)Math.Round(thinner * BorderSymmetryToleranceRatio));

            return thicker - thinner <= tolerance ? thinner : 0;
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
        /// Fit 留白模式下照片在相纸内的可平移像素范围（即白边余量）。
        /// <para>
        /// 不变量：留白模式必须完整保留照片，故平移范围严格等于 <c>相纸尺寸 - 照片尺寸</c>，
        /// 不允许出现负白边。某一方向余量为 0（照片已贴满相纸）时该方向不可平移，
        /// 否则照片会被相纸边缘裁掉，与"留白完整"的语义相悖。
        /// </para>
        /// </summary>
        public static double CalculateFitShiftRange(double paperSize, double scaledSize)
        {
            return Math.Max(0, paperSize - scaledSize);
        }

        /// <summary>
        /// 把归一化偏移量 [-0.5, 0.5] 换算为 Fit 模式下照片左上角坐标，结果始终夹取在相纸内。
        /// <c>-0.5</c> 贴合起始边、<c>0</c> 居中、<c>0.5</c> 贴合结束边，与 Fill 模式的偏移语义完全对称。
        /// </summary>
        public static double CalculateFitImagePosition(double paperSize, double scaledSize, double offset)
        {
            double range = CalculateFitShiftRange(paperSize, scaledSize);
            double position = (range / 2.0) + (Math.Clamp(offset, -0.5, 0.5) * range);
            return Math.Clamp(position, 0, range);
        }

        /// <summary>
        /// <see cref="CalculateFitImagePosition"/> 的逆运算：把照片目标位置反算回归一化偏移量。
        /// 拖拽交互使用它，保证偏移量与画面位置一一对应，避免夹取产生"死区"。
        /// </summary>
        public static double CalculateFitOffsetFromPosition(double paperSize, double scaledSize, double position)
        {
            double range = CalculateFitShiftRange(paperSize, scaledSize);
            if (range <= 0.0001)
            {
                return 0;
            }

            return Math.Clamp((position - (range / 2.0)) / range, -0.5, 0.5);
        }

        /// <summary>
        /// 计算留白完整（Fit）模式下，照片在目标相纸上的等比缩放与自定义白边排版位置
        /// </summary>
        public static FitPlacement CalculateFitPlacement(
            int photoWidth,
            int photoHeight,
            PaperDimensions targetPaper,
            double offsetX = 0,
            double offsetY = 0,
            double cropScale = 1.0)
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
            double baseScale = Math.Min(ratioW, ratioH);
            double effectiveScale = baseScale * Math.Clamp(cropScale, 0.1, 1.0);

            int scaledW = Math.Max(1, (int)Math.Round(photoWidth * effectiveScale, MidpointRounding.AwayFromZero));
            int scaledH = Math.Max(1, (int)Math.Round(photoHeight * effectiveScale, MidpointRounding.AwayFromZero));

            int marginLeft = (int)Math.Round(CalculateFitImagePosition(targetPaper.Width, scaledW, offsetX));
            int marginTop = (int)Math.Round(CalculateFitImagePosition(targetPaper.Height, scaledH, offsetY));

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
                double left = CalculateFitImagePosition(paperW, imgW, offsetX);
                double top = CalculateFitImagePosition(paperH, imgH, offsetY);

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
                // Fit 留白模式：相纸在视口内自适应，照片在相纸内部根据偏移与缩放排版，未覆盖区域为自定义留白
                double paperScale = Math.Min(boxWidth / targetPaper.Width, boxHeight / targetPaper.Height);
                double paperW = Math.Max(10, Math.Round(targetPaper.Width * paperScale));
                double paperH = Math.Max(10, Math.Round(targetPaper.Height * paperScale));

                double scale = Math.Clamp(cropScale, 0.1, 1.0);
                double imgScale = Math.Min(paperW / photoWidth, paperH / photoHeight) * scale;
                double imgW = Math.Max(5, Math.Round(photoWidth * imgScale));
                double imgH = Math.Max(5, Math.Round(photoHeight * imgScale));

                double baseLeft = (paperW - imgW) / 2.0;
                double baseTop = (paperH - imgH) / 2.0;
                double shiftRangeX = Math.Max(paperW - imgW, paperW * 0.8);
                double shiftRangeY = Math.Max(paperH - imgH, paperH * 0.8);

                double imgLeft = Math.Round(baseLeft + (Math.Clamp(offsetX, -0.5, 0.5) * shiftRangeX));
                double imgTop = Math.Round(baseTop + (Math.Clamp(offsetY, -0.5, 0.5) * shiftRangeY));

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

