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

    public readonly record struct ViewportGeometry(
        double PaperWidth,
        double PaperHeight,
        double ImgWidth,
        double ImgHeight,
        double ImgLeft,
        double ImgTop,
        double ExcessW,
        double ExcessH
    );

    public static class ViewportMath
    {
        public static ViewportGeometry Calculate(
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
            if (viewportW <= 20 || viewportH <= 20 || origW <= 0 || origH <= 0 || targetSize == null)
                return default;

            bool isPortrait = origH > origW;
            int baseW = Math.Max(targetSize.PixelWidth, targetSize.PixelHeight);
            int baseH = Math.Min(targetSize.PixelWidth, targetSize.PixelHeight);

            int targetW = isPortrait ? baseH : baseW;
            int targetH = isPortrait ? baseW : baseH;

            // 视口预留 40 像素边距
            double maxW = Math.Max(50, viewportW - 40);
            double maxH = Math.Max(50, viewportH - 40);

            double paperScale = Math.Min(maxW / targetW, maxH / targetH);
            double paperW = targetW * paperScale;
            double paperH = targetH * paperScale;

            double imgScale, imgW, imgH, excessW, excessH, imgLeft, imgTop;

            if (mode == CutMode.Fill)
            {
                imgScale = Math.Max(paperW / origW, paperH / origH);
                imgW = origW * imgScale;
                imgH = origH * imgScale;
                excessW = Math.Max(0, imgW - paperW);
                excessH = Math.Max(0, imgH - paperH);
                imgLeft = -(excessW / 2.0) - (offsetX * excessW);
                imgTop = -(excessH / 2.0) - (offsetY * excessH);
            }
            else
            {
                imgScale = Math.Min(paperW / origW, paperH / origH);
                imgW = origW * imgScale;
                imgH = origH * imgScale;
                excessW = 0;
                excessH = 0;
                imgLeft = (paperW - imgW) / 2.0;
                imgTop = (paperH - imgH) / 2.0;
            }

            return new ViewportGeometry(paperW, paperH, imgW, imgH, imgLeft, imgTop, excessW, excessH);
        }
    }

    public class ViewportPaperSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 6) return 200.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return 200.0;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            PhotoSize size = values[4] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[5] is CutMode m ? m : CutMode.Fill;

            var geo = ViewportMath.Calculate(vw, vh, origW, origH, 0, 0, size, mode);
            return parameter?.ToString() == "Width" ? Math.Max(10, geo.PaperWidth) : Math.Max(10, geo.PaperHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ViewportImgSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 8) return 200.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return 200.0;
            }

            double vw = values[0] is double w ? w : 400;
            double vh = values[1] is double h ? h : 300;
            int origW = values[2] is int ow ? ow : 1;
            int origH = values[3] is int oh ? oh : 1;
            double ox = values[4] is double x ? x : 0;
            double oy = values[5] is double y ? y : 0;
            PhotoSize size = values[6] is PhotoSize ps ? ps : PhotoSize.Inch6;
            CutMode mode = values[7] is CutMode m ? m : CutMode.Fill;

            var geo = ViewportMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return parameter?.ToString() == "Width" ? Math.Max(1, geo.ImgWidth) : Math.Max(1, geo.ImgHeight);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ViewportImgMarginConverter : IMultiValueConverter
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

            var geo = ViewportMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode);
            return new Thickness(geo.ImgLeft, geo.ImgTop, 0, 0);
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CropMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (!CropMath.TryParseArgs(values, out double containerSize, out int origW, out int origH, out double ox, out double oy, out PhotoSize size, out CutMode mode))
                return new Thickness(0);

            var geo = CropMath.Calculate(containerSize, origW, origH, ox, oy, size, mode);
            return new Thickness(geo.CropX, geo.CropY, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CropSizeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (!CropMath.TryParseArgs(values, out double containerSize, out int origW, out int origH, out double ox, out double oy, out PhotoSize size, out CutMode mode))
                return 0.0;

            var geo = CropMath.Calculate(containerSize, origW, origH, ox, oy, size, mode);
            return parameter?.ToString() == "Width" ? geo.CropWidth : geo.CropHeight;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CropMaskConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
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

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return Enum.Parse(targetType, (string)parameter);
            }
            return Binding.DoNothing;
        }
    }
}
