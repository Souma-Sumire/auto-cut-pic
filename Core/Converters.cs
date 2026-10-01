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
            if (origW <= 0 || origH <= 0 || containerSize <= 0)
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

    public class ZoomToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double zoom = (double)value;
            return 150 * zoom;
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
