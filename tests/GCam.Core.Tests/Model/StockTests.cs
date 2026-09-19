using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class StockTests
    {
        // A 100 x 60 x 25 block sitting with its bottom on Z0.
        private static Bounds SampleModel()
        {
            return new Bounds(new Vec3(0, 0, 0), new Vec3(100, 60, 25));
        }

        [Fact]
        public void A_relative_box_grows_the_model_by_the_side_offset_on_all_four_sides()
        {
            var stock = new Stock
            {
                Mode = StockMode.RelativeBox,
                SideOffset = 2,
                TopOffset = 1,
                BottomOffset = 0.5,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(-2, box.Min.X, 6);
            Assert.Equal(-2, box.Min.Y, 6);
            Assert.Equal(102, box.Max.X, 6);
            Assert.Equal(62, box.Max.Y, 6);
        }

        [Fact]
        public void Top_and_bottom_offsets_are_applied_the_right_way_up()
        {
            // Getting these the wrong way round produces stock that looks the right size
            // and has Z0 in the wrong place, which is the expensive kind of wrong.
            var stock = new Stock
            {
                Mode = StockMode.RelativeBox,
                TopOffset = 1,
                BottomOffset = 5,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(-5, box.Min.Z, 6);
            Assert.Equal(26, box.Max.Z, 6);
        }

        [Fact]
        public void The_XY_mode_offsets_the_two_axes_independently()
        {
            var stock = new Stock
            {
                Mode = StockMode.RelativeBoxXY,
                OffsetX = 2,
                OffsetY = 10,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(104, box.Size.X, 6);
            Assert.Equal(80, box.Size.Y, 6);
        }

        [Fact]
        public void The_XY_mode_ignores_the_shared_side_offset()
        {
            // Switching mode must not silently keep applying the other mode's field.
            var stock = new Stock
            {
                Mode = StockMode.RelativeBoxXY,
                SideOffset = 999,
                OffsetX = 1,
                OffsetY = 1,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(102, box.Size.X, 6);
        }

        [Fact]
        public void A_fixed_box_takes_its_size_from_the_parameters_not_the_model()
        {
            var stock = new Stock
            {
                Mode = StockMode.FixedSizeBox,
                Width = 150,
                Depth = 80,
                Height = 30,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(150, box.Size.X, 6);
            Assert.Equal(80, box.Size.Y, 6);
            Assert.Equal(30, box.Size.Z, 6);
        }

        [Fact]
        public void A_fixed_box_centres_the_model_in_X_and_Y()
        {
            var stock = new Stock
            {
                Mode = StockMode.FixedSizeBox,
                Width = 150,
                Depth = 80,
                Height = 30,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(SampleModel().Centre.X, box.Centre.X, 6);
            Assert.Equal(SampleModel().Centre.Y, box.Centre.Y, 6);
        }

        [Fact]
        public void A_fixed_box_keeps_its_top_at_the_top_of_the_model()
        {
            // Top alignment rather than centring in Z, so that Z0 does not move when the
            // stock height is changed.
            var stock = new Stock
            {
                Mode = StockMode.FixedSizeBox,
                Width = 150,
                Depth = 80,
                Height = 30,
            };

            Bounds box = stock.ComputeBounds(SampleModel());

            Assert.Equal(25, box.Max.Z, 6);
            Assert.Equal(-5, box.Min.Z, 6);
        }

        [Fact]
        public void Stock_that_is_smaller_than_the_model_does_not_fit_around_it()
        {
            var stock = new Stock
            {
                Mode = StockMode.FixedSizeBox,
                Width = 50,
                Depth = 80,
                Height = 30,
            };

            Assert.False(stock.FitsAround(SampleModel()));
        }

        [Fact]
        public void Zero_offsets_fit_around_the_model_exactly()
        {
            // Machining right to the model surface is legitimate, so this must not be
            // treated as too small.
            var stock = new Stock { Mode = StockMode.RelativeBox };

            Assert.True(stock.FitsAround(SampleModel()));
        }

        [Fact]
        public void Negative_offsets_are_allowed_because_stock_can_sit_inside_the_model()
        {
            // A part already roughed elsewhere. Rejected until 2026-09-19.
            var stock = new Stock { Mode = StockMode.RelativeBox, SideOffset = -1 };

            Assert.Empty(stock.Validate());
        }

        [Fact]
        public void Zero_offsets_are_accepted()
        {
            var stock = new Stock { Mode = StockMode.RelativeBox };

            Assert.Empty(stock.Validate());
        }

        [Fact]
        public void A_fixed_box_with_no_size_is_rejected()
        {
            var stock = new Stock { Mode = StockMode.FixedSizeBox };

            Assert.Equal(3, stock.Validate().Count);
        }

        [Fact]
        public void Validation_only_looks_at_the_fields_the_current_mode_uses()
        {
            // The unused fields keep their values so switching mode and back loses
            // nothing - but they must not make the stock invalid in the meantime.
            var stock = new Stock
            {
                Mode = StockMode.RelativeBox,
                SideOffset = 2,
                Width = 0,
            };

            Assert.Empty(stock.Validate());
        }

        [Fact]
        public void Clone_is_independent_of_the_original()
        {
            var stock = new Stock { SideOffset = 2 };

            Stock copy = stock.Clone();
            copy.SideOffset = 99;

            Assert.Equal(2, stock.SideOffset, 6);
        }
    }
}
