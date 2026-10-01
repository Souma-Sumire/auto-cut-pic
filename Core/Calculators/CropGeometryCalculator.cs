using System;

namespace AutoCutPic.Core.Calculators
{
    /// <summary>
    /// 核心照片几何与冲印相纸裁切计算器（无外部依赖纯算法类）
    /// </summary>
    public static class CropGeometryCalculator
    {
        /// <summary>
        /// 根据照片物理宽高，自适应计算目标冲印相纸像素尺寸（横竖构图对齐）
        /// </summary>
        public static PaperDimensions CalculateTargetPaperDimensions(PhotoSize targetSize, int photoWidth, int photoHeight)
        {
            if (photoWidth <= 0 || photoHeight <= 0)
            {
                return new PaperDimensions(targetSize.PixelWidth, targetSize.PixelHeight);
            }

            int baseW = Math.Max(targetSize.PixelWidth, targetSize.PixelHeight);
            int baseH = Math.Min(targetSize.PixelWidth, targetSize.PixelHeight);

            bool photoIsPortrait = photoHeight > photoWidth;
            return photoIsPortrait
                ? new PaperDimensions(baseH, baseW)
                : new PaperDimensions(baseW, baseH);
        }

        /// <summary>
        /// 计算裁切填满（Fill）模式下，原图物理坐标系中的裁切窗口
        /// </summary>
        public static CropRect CalculateFillCrop(
            int photoWidth,
            int photoHeight,
            PaperDimensions targetPaper,
            double offsetX = 0,
            double offsetY = 0)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetPaper.Width <= 0 || targetPaper.Height <= 0)
            {
                return new CropRect(0, 0, Math.Max(1, photoWidth), Math.Max(1, photoHeight));
            }

            double targetAR = (double)targetPaper.Width / targetPaper.Height;
            double photoAR = (double)photoWidth / photoHeight;

            int cropW;
            int cropH;
            int cropX;
            int cropY;

            double clampedOffsetX = Math.Clamp(offsetX, -0.5, 0.5);
            double clampedOffsetY = Math.Clamp(offsetY, -0.5, 0.5);

            if (photoAR > targetAR)
            {
                cropH = photoHeight;
                cropW = (int)Math.Round(cropH * targetAR, MidpointRounding.AwayFromZero);
                cropW = Math.Clamp(cropW, 1, photoWidth);

                int excessW = photoWidth - cropW;
                cropX = (int)Math.Round((excessW / 2.0) + (clampedOffsetX * excessW), MidpointRounding.AwayFromZero);
                cropY = 0;
            }
            else
            {
                cropW = photoWidth;
                cropH = (int)Math.Round(cropW / targetAR, MidpointRounding.AwayFromZero);
                cropH = Math.Clamp(cropH, 1, photoHeight);

                int excessH = photoHeight - cropH;
                cropX = 0;
                cropY = (int)Math.Round((excessH / 2.0) + (clampedOffsetY * excessH), MidpointRounding.AwayFromZero);
            }

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
            double offsetY = 0)
        {
            if (cardBoxWidth <= 10 || cardBoxHeight <= 10 || photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
            {
                return new BatchCardLayout(cardBoxWidth, cardBoxHeight, cardBoxWidth, cardBoxHeight, 0, 0, false);
            }

            var targetPaper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight);
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
        /// </summary>
        public static BatchCardCropLayout CalculateBatchCardCropLayout(
            double boxWidth,
            double boxHeight,
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize,
            CutMode mode,
            double offsetX = 0,
            double offsetY = 0)
        {
            if (boxWidth <= 10 || boxHeight <= 10 || photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
            {
                return new BatchCardCropLayout(boxWidth, boxHeight, boxWidth, boxHeight, 0, 0, 0, 0, boxWidth, boxHeight, false);
            }

            var targetPaper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight);

            if (mode == CutMode.Fit)
            {
                // Fit 留白模式：相纸在视口内居中自适应，照片在相纸内部居中留白
                double paperScale = Math.Min(boxWidth / targetPaper.Width, boxHeight / targetPaper.Height);
                double paperW = Math.Max(10, Math.Round(targetPaper.Width * paperScale));
                double paperH = Math.Max(10, Math.Round(targetPaper.Height * paperScale));
                double paperLeft = Math.Round((boxWidth - paperW) / 2.0);
                double paperTop = Math.Round((boxHeight - paperH) / 2.0);

                double imgScale = Math.Min(paperW / photoWidth, paperH / photoHeight);
                double imgW = Math.Max(5, Math.Round(photoWidth * imgScale));
                double imgH = Math.Max(5, Math.Round(photoHeight * imgScale));
                double imgLeft = paperLeft + Math.Round((paperW - imgW) / 2.0);
                double imgTop = paperTop + Math.Round((paperH - imgH) / 2.0);

                return new BatchCardCropLayout(
                    boxWidth, boxHeight,
                    imgW, imgH, imgLeft, imgTop,
                    paperLeft, paperTop, paperW, paperH,
                    true
                );
            }
            else
            {
                // Fill 填充模式：原图完整展示在 box 内部，在其上清晰高亮标出裁切框与外部被裁暗区
                double scale = Math.Min(boxWidth / photoWidth, boxHeight / photoHeight);
                double imgW = Math.Max(10, Math.Round(photoWidth * scale));
                double imgH = Math.Max(10, Math.Round(photoHeight * scale));
                double imgLeft = Math.Round((boxWidth - imgW) / 2.0);
                double imgTop = Math.Round((boxHeight - imgH) / 2.0);

                var cropRect = CalculateFillCrop(photoWidth, photoHeight, targetPaper, offsetX, offsetY);

                double cropW = Math.Max(5, Math.Round(cropRect.Width * scale));
                double cropH = Math.Max(5, Math.Round(cropRect.Height * scale));
                double cropLeft = imgLeft + Math.Round(cropRect.X * scale);
                double cropTop = imgTop + Math.Round(cropRect.Y * scale);

                return new BatchCardCropLayout(
                    boxWidth, boxHeight,
                    imgW, imgH, imgLeft, imgTop,
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
            double tolerance = 0.015)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
                return false;

            var paper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight);
            double targetAR = (double)paper.Width / paper.Height;
            double photoAR = (double)photoWidth / photoHeight;

            double diff = Math.Abs(photoAR - targetAR) / targetAR;
            return diff <= tolerance;
        }

        /// <summary>
        /// 计算在填充（Fill）模式下，原图被裁剪抛弃的面积占原图总面积的比例
        /// </summary>
        public static double CalculateCropLossRatio(
            int photoWidth,
            int photoHeight,
            PhotoSize targetSize)
        {
            if (photoWidth <= 0 || photoHeight <= 0 || targetSize == null)
                return 0.0;

            var paper = CalculateTargetPaperDimensions(targetSize, photoWidth, photoHeight);
            double targetAR = (double)paper.Width / paper.Height;
            double photoAR = (double)photoWidth / photoHeight;

            if (photoAR > targetAR)
            {
                // 横向裁剪：宽度超出，裁剪后实际可见宽度为 photoHeight * targetAR
                double visibleW = photoHeight * targetAR;
                return Math.Max(0.0, 1.0 - (visibleW / photoWidth));
            }
            else
            {
                // 纵向裁剪：高度超出，裁剪后实际可见高度为 photoWidth / targetAR
                double visibleH = photoWidth / targetAR;
                return Math.Max(0.0, 1.0 - (visibleH / photoHeight));
            }
        }
    }
}

