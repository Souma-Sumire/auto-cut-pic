using AutoCutPic.Core;
using Xunit;

namespace AutoCutPic.Tests
{
    public class PhotoSizeTests
    {
        [Fact]
        public void Inch6_ShouldCalculateAccurate300DpiPixels()
        {
            var size = PhotoSize.Inch6;

            Assert.Equal("6寸", size.Name);
            Assert.Equal(15.2, size.WidthCm);
            Assert.Equal(10.2, size.HeightCm);
            Assert.Equal(300, size.Dpi);

            // 15.2 / 2.54 * 300 = 1795.2755 -> 1795
            Assert.Equal(1795, size.PixelWidth);
            // 10.2 / 2.54 * 300 = 1204.7244 -> 1205
            Assert.Equal(1205, size.PixelHeight);
        }

        [Fact]
        public void Inch5_ShouldCalculateAccurate300DpiPixels()
        {
            var size = PhotoSize.Inch5;

            Assert.Equal("5寸", size.Name);
            // 12.7 / 2.54 * 300 = 1500
            Assert.Equal(1500, size.PixelWidth);
            // 8.9 / 2.54 * 300 = 1051.18 -> 1051
            Assert.Equal(1051, size.PixelHeight);
        }

        [Fact]
        public void Inch7_ShouldCalculateAccurate300DpiPixels()
        {
            var size = PhotoSize.Inch7;

            Assert.Equal("7寸", size.Name);
            // 17.8 / 2.54 * 300 = 2102.36 -> 2102
            Assert.Equal(2102, size.PixelWidth);
            // 12.7 / 2.54 * 300 = 1500
            Assert.Equal(1500, size.PixelHeight);
        }

        [Fact]
        public void Inch8_ShouldCalculateAccurate300DpiPixels()
        {
            var size = PhotoSize.Inch8;

            Assert.Equal("8寸", size.Name);
            // 20.3 / 2.54 * 300 = 2397.63 -> 2398
            Assert.Equal(2398, size.PixelWidth);
            // 15.2 / 2.54 * 300 = 1795.27 -> 1795
            Assert.Equal(1795, size.PixelHeight);
        }
    }
}
