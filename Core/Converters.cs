using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoCutPic.Core
{
    public readonly record struct CropBoxGeometry(
        double CropX,
        double CropY,
        double CropWidth,
        double CropHeight,
        double DispLeft,
        double DispTop,
        double DispWidth,
        double DispHeight
    );

    public static class CropMath
    {
        public static CropBoxGeometry Calculate(
            double containerSize,
            int origW,
            int origH,
            double offsetX,
            double offsetY,
            PhotoSize targetSize,
            CutMode mode
        )
        {
            if (origW <= 0 || origH <= 0 || containerSize <= 0 || targetSize == null)
                return default;

            bool originIsPortrait = origH > origW;
            int baseW = Math.Max(targetSize.PixelWidth, targetSize.PixelHeight);
            int baseH = Math.Min(targetSize.PixelWidth, targetSize.PixelHeight);

            int targetW = originIsPortrait ? baseH : baseW;
            int targetH = originIsPortrait ? baseW : baseH;

            double targetAR = (double)targetW / targetH;
            double photoAR = (double)origW / origH;

            double scale = Math.Min(containerSize / origW, containerSize / origH);
            double dispW = origW * scale;
            double dispH = origH * scale;
            double dispLeft = (containerSize - dispW) / 2.0;
            double dispTop = (containerSize - dispH) / 2.0;

            double cropW, cropH, cropX, cropY;

            if (mode == CutMode.Fill)
            {
                if (photoAR > targetAR)
                {
                    cropH = dispH;
                    cropW = dispH * targetAR;
                    double excessW = dispW - cropW;
                    cropX = dispLeft + (excessW / 2.0) + (offsetX * excessW);
                    cropY = dispTop;
                }
                else
                {
                    cropW = dispW;
                    cropH = dispW / targetAR;
                    double excessH = dispH - cropH;
                    cropX = dispLeft;
                    cropY = dispTop + (excessH / 2.0) + (offsetY * excessH);
                }
            }
            else
            {
                cropW = dispW;
                cropH = dispH;
                cropX = dispLeft;
                cropY = dispTop;
            }

            cropX = Math.Max(dispLeft, Math.Min(cropX, dispLeft + dispW - cropW));
            cropY = Math.Max(dispTop, Math.Min(cropY, dispTop + dispH - cropH));

            return new CropBoxGeometry(cropX, cropY, cropW, cropH, dispLeft, dispTop, dispW, dispH);
        }

        public static bool TryParseArgs(
            object[] values,
            out double containerSize,
            out int origW,
            out int origH,
            out double offsetX,
            out double offsetY,
            out PhotoSize targetSize,
            out CutMode mode
        )
        {
            containerSize = 0;
            origW = 0;
            origH = 0;
            offsetX = 0;
            offsetY = 0;
            targetSize = PhotoSize.Inch6;
            mode = CutMode.Fill;

            if (values == null || values.Length < 7)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return false;
            }

            if (values[0] is double d && d > 0)
                containerSize = d;
            else
                return false;

            if (values[1] is int w) origW = w;
            if (values[2] is int h) origH = h;
            if (values[3] is double ox) offsetX = ox;
            if (values[4] is double oy) offsetY = oy;
            if (values[5] is PhotoSize size) targetSize = size;
            if (values[6] is CutMode m) mode = m;

            return origW > 0 && origH > 0;
        }
    }

    public readonly record struct PsWorkbenchGeometry(
        double BoardWidth,
        double BoardHeight,
        double PaperWidth,
        double PaperHeight,
        double PaperX,
        double PaperY,
        double ImgWidth,
        double ImgHeight,
        double ImgX,
        double ImgY,
        double ExcessW,
        double ExcessH,
        bool IsFitMode
    );

    public static class PsWorkbenchMath
    {
        public static PsWorkbenchGeometry Calculate(
            double viewportW,
            double viewportH,
            int origW,
            int origH,
            double offsetX,
            double offsetY,
            PhotoSize targetSize,
            CutMode mode
        )
        {
            if (viewportW <= 40 || viewportH <= 40 || origW <= 0 || origH <= 0 || targetSize == null)
                return default;

            bool isPortrait = origH > origW;
            int baseW = Math.Max(targetSize.PixelWidth, targetSize.PixelHeight);
            int baseH = Math.Min(targetSize.PixelWidth, targetSize.PixelHeight);
            int targetW = isPortrait ? baseH : baseW;
            int targetH = isPortrait ? baseW : baseH;

            double targetAR = (double)targetW / targetH;
            double photoAR = (double)origW / origH;

            // 预留工作台四周安全边距 60px
            double availW = Math.Max(50, viewportW - 60);
            double availH = Math.Max(50, viewportH - 60);

            double boardW, boardH, paperW, paperH, paperX, paperY, imgW, imgH, imgX, imgY, excessW, excessH;

            if (mode == CutMode.Fit)
            {
                // Fit 留白模式：相纸在视口内居中自适应，照片在相纸内部居中，露出的相纸底色为纯白白边
                double paperScale = Math.Min(availW / targetW, availH / targetH);
                paperW = Math.Max(10, targetW * paperScale);
                paperH = Math.Max(10, targetH * paperScale);

                double imgScale = Math.Min(paperW / origW, paperH / origH);
                imgW = Math.Max(10, origW * imgScale);
                imgH = Math.Max(10, origH * imgScale);

                boardW = paperW;
                boardH = paperH;
                paperX = 0;
                paperY = 0;

                imgX = (paperW - imgW) / 2.0;
                imgY = (paperH - imgH) / 2.0;

                excessW = 0;
                excessH = 0;
            }
            else
            {
                // Fill 裁剪填充模式：照片在视口内最大化居中呈现，相纸在照片内部裁剪取景，相纸外部半透明暗色遮罩
                double photoScale = Math.Min(availW / origW, availH / origH);
                imgW = Math.Max(10, origW * photoScale);
                imgH = Math.Max(10, origH * photoScale);

                boardW = imgW;
                boardH = imgH;
                imgX = 0;
                imgY = 0;

                if (photoAR > targetAR)
                {
                    // 原图比相纸更宽：高度贴满相纸，宽度裁切
                    paperH = imgH;
                    paperW = imgH * targetAR;
                    excessW = Math.Max(0, imgW - paperW);
                    excessH = 0;
                    paperX = (excessW / 2.0) + (offsetX * excessW);
                    paperY = 0;
                }
                else
                {
                    // 原图比相纸更高：宽度贴满相纸，高度裁切
                    paperW = imgW;
                    paperH = imgW / targetAR;
                    excessW = 0;
                    excessH = Math.Max(0, imgH - paperH);
                    paperX = 0;
                    paperY = (excessH / 2.0) + (offsetY * excessH);
                }

                paperX = Math.Max(0, Math.Min(paperX, imgW - paperW));
                paperY = Math.Max(0, Math.Min(paperY, imgH - paperH));
            }

            return new PsWorkbenchGeometry(
                boardW, boardH,
                paperW, paperH,
                paperX, paperY,
                imgW, imgH,
                imgX, imgY,
                excessW, excessH,
                mode == CutMode.Fit
            );
        }
    }

    public class PsBoardSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 6) return 300.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return 300.0;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            PhotoSize size = values[4] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[5] is CutMode m ? m : CutMode.Fill;

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, 0, 0, size, mode);
            return parameter?.ToString() == "Width" ? Math.Max(10, geo.BoardWidth) : Math.Max(10, geo.BoardHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PsPaperSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return 300.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return 300.0;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return parameter?.ToString() == "Width" ? Math.Max(10, geo.PaperWidth) : Math.Max(10, geo.PaperHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PsPaperMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return new Thickness(0);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return new Thickness(0);
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return new Thickness(geo.PaperX, geo.PaperY, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PsImgSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return 300.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return 300.0;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return parameter?.ToString() == "Width" ? Math.Max(10, geo.ImgWidth) : Math.Max(10, geo.ImgHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PsImgMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return new Thickness(0);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return new Thickness(0);
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return new Thickness(geo.ImgX, geo.ImgY, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PsCropMaskConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return Geometry.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return Geometry.Empty;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            if (mode == CutMode.Fit)
                return Geometry.Empty; // 留白模式下遮罩为空，完整呈现相纸与纯白留白边

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            if (geo.BoardWidth <= 0 || geo.BoardHeight <= 0)
                return Geometry.Empty;

            var fullRect = new RectangleGeometry(new Rect(0, 0, geo.BoardWidth, geo.BoardHeight));
            var cropRect = new RectangleGeometry(new Rect(geo.PaperX, geo.PaperY, geo.PaperWidth, geo.PaperHeight));
            return new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cropRect);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ZoomToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double zoom = (double)value;
            return 100 * zoom;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class CropMarginConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (!CropMath.TryParseArgs(values, out double containerSize, out int origW, out int origH, out double ox, out double oy, out PhotoSize size, out CutMode mode))
                return new Thickness(0);

            var geo = CropMath.Calculate(containerSize, origW, origH, ox, oy, size, mode);
            return new Thickness(geo.CropX, geo.CropY, 0, 0);
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class CropSizeConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (!CropMath.TryParseArgs(values, out double containerSize, out int origW, out int origH, out double ox, out double oy, out PhotoSize size, out CutMode mode))
                return 0.0;

            var geo = CropMath.Calculate(containerSize, origW, origH, ox, oy, size, mode);
            return parameter?.ToString() == "Width" ? geo.CropWidth : geo.CropHeight;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class CropMaskConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (!CropMath.TryParseArgs(values, out double containerSize, out int origW, out int origH, out double ox, out double oy, out PhotoSize size, out CutMode mode))
                return Geometry.Empty;

            if (mode == CutMode.Fit)
                return Geometry.Empty;

            var geo = CropMath.Calculate(containerSize, origW, origH, ox, oy, size, mode);
            if (geo.DispWidth <= 0 || geo.DispHeight <= 0)
                return Geometry.Empty;

            var fullRect = new RectangleGeometry(new Rect(geo.DispLeft, geo.DispTop, geo.DispWidth, geo.DispHeight));
            var cropRect = new RectangleGeometry(new Rect(geo.CropX, geo.CropY, geo.CropWidth, geo.CropHeight));
            return new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cropRect);
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (value is bool b && b)
            {
                return Enum.Parse(targetType, (string)parameter);
            }
            return Binding.DoNothing;
        }
    }
}
