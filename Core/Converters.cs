using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoCutPic.Core.Calculators;

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
            CutMode mode,
            TargetOrientation? orientation = null
        )
        {
            if (viewportW <= 40 || viewportH <= 40 || origW <= 0 || origH <= 0 || targetSize == null)
                return default;

            var targetPaper = AutoCutPic.Core.Calculators.CropGeometryCalculator.CalculateTargetPaperDimensions(targetSize, origW, origH, orientation);
            int targetW = targetPaper.Width;
            int targetH = targetPaper.Height;
            double targetAR = targetPaper.AspectRatio;
            double photoAR = (double)origW / origH;

            // 预留工作台四周充足安全边距 120px，确保画面呼吸感与专业暗房视野，杜绝贴边压迫
            double availW = Math.Max(50, viewportW - 120);
            double availH = Math.Max(50, viewportH - 120);

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

            TargetOrientation? orientation = null;
            for (int i = 6; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, 0, 0, size, mode, orientation);
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

            TargetOrientation? orientation = null;
            for (int i = 8; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode, orientation);
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

            TargetOrientation? orientation = null;
            for (int i = 8; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode, orientation);
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

            TargetOrientation? orientation = null;
            for (int i = 8; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode, orientation);
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

            TargetOrientation? orientation = null;
            for (int i = 8; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode, orientation);
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

            TargetOrientation? orientation = null;
            for (int i = 8; i < values.Length; i++)
            {
                if (values[i] is TargetOrientation to)
                {
                    orientation = to;
                    break;
                }
            }

            if (mode == CutMode.Fit)
                return Geometry.Empty; // 留白模式下遮罩为空，完整呈现相纸与纯白留白边

            var geo = PsWorkbenchMath.Calculate(vw, vh, origW, origH, ox, oy, size, mode, orientation);
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

    public class BatchCardPlacementConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Length < 6)
                return GetDefault(parameter);

            for (int i = 0; i < 6; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return GetDefault(parameter);
            }

            int photoW = values[0] is int pw ? pw : 0;
            int photoH = values[1] is int ph ? ph : 0;
            PhotoSize? size = values[2] as PhotoSize;
            CutMode mode = values[3] is CutMode m ? m : CutMode.Fill;
            double offsetX = values[4] is double ox ? ox : 0.0;
            double offsetY = values[5] is double oy ? oy : 0.0;

            double cardW = 190.0;
            double cardH = 135.0;

            if (values.Length > 7 && values[6] is double cw && cw > 10 && values[7] is double ch && ch > 10)
            {
                cardW = cw;
                cardH = ch;
            }

            if (photoW <= 0 || photoH <= 0 || size == null)
                return GetDefault(parameter);

            var layout = AutoCutPic.Core.Calculators.CropGeometryCalculator.CalculateBatchCardLayout(
                cardW,
                cardH,
                photoW,
                photoH,
                size,
                mode,
                offsetX,
                offsetY
            );

            string param = parameter?.ToString() ?? "";
            return param switch
            {
                "PaperWidth" => layout.PaperWidth,
                "PaperHeight" => layout.PaperHeight,
                "ImageWidth" => layout.ImageWidth,
                "ImageHeight" => layout.ImageHeight,
                "ImageMargin" => new Thickness(layout.MarginLeft, layout.MarginTop, 0, 0),
                _ => 0.0
            };
        }

        private static object GetDefault(object? parameter)
        {
            return parameter?.ToString() == "ImageMargin" ? new Thickness(0) : 0.0;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class BatchCardCropConverter : IMultiValueConverter
    {
        public object Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (values == null || values.Length < 6)
                return GetDefault(parameter);

            for (int i = 0; i < 6; i++)
            {
                if (values[i] == DependencyProperty.UnsetValue || values[i] == null)
                    return GetDefault(parameter);
            }

            int photoW = values[0] is int pw ? pw : 0;
            int photoH = values[1] is int ph ? ph : 0;
            PhotoSize? size = values[2] as PhotoSize;
            CutMode mode = values[3] is CutMode m ? m : CutMode.Fill;
            double offsetX = values[4] is double ox ? ox : 0.0;
            double offsetY = values[5] is double oy ? oy : 0.0;

            TargetOrientation? orientation = null;
            double boxW = 128.0;
            double boxH = 88.0;
            var doubleParams = new List<double>();

            if (values.Length > 6)
            {
                for (int i = 6; i < values.Length; i++)
                {
                    if (values[i] is TargetOrientation to)
                    {
                        orientation = to;
                    }
                    else if (values[i] is double d && d > 10)
                    {
                        doubleParams.Add(d);
                    }
                }
            }

            if (doubleParams.Count >= 2)
            {
                boxW = doubleParams[0];
                boxH = doubleParams[1];
            }
            else if (doubleParams.Count == 1)
            {
                boxW = doubleParams[0];
                boxH = doubleParams[0] * 0.68;
            }

            if (photoW <= 0 || photoH <= 0 || size == null)
                return GetDefault(parameter);

            var layout = AutoCutPic.Core.Calculators.CropGeometryCalculator.CalculateBatchCardCropLayout(
                boxW,
                boxH,
                photoW,
                photoH,
                size,
                mode,
                offsetX,
                offsetY,
                orientation
            );

            string param = parameter?.ToString() ?? "";
            return param switch
            {
                "ContainerWidth" => layout.BoxWidth,
                "ContainerHeight" => layout.BoxHeight,
                "ImageWidth" => layout.ImageWidth,
                "ImageHeight" => layout.ImageHeight,
                "ImageMargin" => new Thickness(layout.ImageLeft, layout.ImageTop, 0, 0),
                "CropWidth" => layout.CropWidth,
                "CropHeight" => layout.CropHeight,
                "CropMargin" => new Thickness(layout.CropLeft, layout.CropTop, 0, 0),
                "MaskGeometry" => layout.IsFit
                    ? Geometry.Empty
                    : new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(layout.ImageLeft, layout.ImageTop, layout.ImageWidth, layout.ImageHeight)),
                        new RectangleGeometry(new Rect(layout.CropLeft, layout.CropTop, layout.CropWidth, layout.CropHeight))
                    ),
                "CropVisibility" => layout.IsFit ? Visibility.Collapsed : Visibility.Visible,
                "FitPaperVisibility" => layout.IsFit ? Visibility.Visible : Visibility.Collapsed,
                _ => 0.0
            };
        }

        private static object GetDefault(object? parameter)
        {
            string p = parameter?.ToString() ?? "";
            if (p.EndsWith("Margin", StringComparison.Ordinal))
                return new Thickness(0);
            if (p == "MaskGeometry")
                return Geometry.Empty;
            if (p.EndsWith("Visibility", StringComparison.Ordinal))
                return Visibility.Collapsed;
            return 0.0;
        }

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();
    }

    public class CutModeToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CutMode mode)
            {
                return mode == CutMode.Fill ? "裁剪填充" : "留白完整";
            }
            return "-";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class OrientationToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TargetOrientation o)
            {
                return o == TargetOrientation.Landscape ? "横向相纸" : "纵向相纸";
            }
            return "横向相纸";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// 当开启免修淡化且照片为同比例时，返回 0.38 不透明度（类似 Windows 剪切淡化视觉），否则返回 1.0
    /// </summary>
    public class DimMatchedOpacityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 1.0;

            bool isMatched = values[0] is bool m && m;
            bool dimEnabled = values[1] is bool d && d;

            return (isMatched && dimEnabled) ? 0.38 : 1.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// 当开启免修隐藏且照片为同比例时，返回 Collapsed，否则返回 Visible
    /// </summary>
    public class HideMatchedVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return Visibility.Visible;

            bool isMatched = values[0] is bool m && m;
            bool hideEnabled = values[1] is bool h && h;

            return (isMatched && hideEnabled) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}

