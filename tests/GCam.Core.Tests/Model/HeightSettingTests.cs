using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using Xunit;

namespace GCam.Core.Tests.Model
{
    public class HeightSettingTests
    {
        // Stock from Z -5 to Z 30, model from Z 0 to Z 25.
        private static HeightContext Context(
            IReadOnlyDictionary<string, double> selections = null)
        {
            return new HeightContext(30, -5, 25, 0, selections);
        }

        [Theory]
        [InlineData(HeightMode.FromStockTop, 30)]
        [InlineData(HeightMode.FromStockBottom, -5)]
        [InlineData(HeightMode.FromModelTop, 25)]
        [InlineData(HeightMode.FromModelBottom, 0)]
        [InlineData(HeightMode.FromJobOrigin, 0)]
        public void Each_mode_measures_from_its_own_datum(HeightMode mode, double expected)
        {
            var height = new HeightSetting(mode);

            Assert.True(height.TryResolve(Context(), out double z));
            Assert.Equal(expected, z, 9);
        }

        [Fact]
        public void The_offset_is_signed()
        {
            // "Model bottom, -0.5mm" is how a through cut is expressed, so a negative
            // offset has to go below the datum rather than being treated as a distance.
            var above = new HeightSetting(HeightMode.FromStockTop, 10);
            var below = new HeightSetting(HeightMode.FromModelBottom, -0.5);

            above.TryResolve(Context(), out double high);
            below.TryResolve(Context(), out double low);

            Assert.Equal(40, high, 9);
            Assert.Equal(-0.5, low, 9);
        }

        [Fact]
        public void The_job_origin_mode_ignores_the_stock_and_the_model()
        {
            var height = new HeightSetting(HeightMode.FromJobOrigin, 3);

            // Same setting, wildly different stock: the answer must not move.
            height.TryResolve(new HeightContext(30, -5, 25, 0), out double a);
            height.TryResolve(new HeightContext(900, -900, 800, -800), out double b);

            Assert.Equal(3, a, 9);
            Assert.Equal(3, b, 9);
        }

        [Fact]
        public void A_selection_resolves_through_the_context()
        {
            var height = new HeightSetting(HeightMode.FromSelection, 1)
            {
                Reference = new GeometryRef
                {
                    PersistentId = "face-1",
                    Kind = GeometryRefKind.Face,
                    DisplayName = "Top face",
                },
            };

            var selections = new Dictionary<string, double> { ["face-1"] = 12.5 };

            Assert.True(height.TryResolve(Context(selections), out double z));
            Assert.Equal(13.5, z, 9);
        }

        [Fact]
        public void A_selection_mode_with_nothing_selected_does_not_resolve()
        {
            var height = new HeightSetting(HeightMode.FromSelection);

            Assert.False(height.TryResolve(Context(), out double _));
            Assert.Contains("nothing is selected", height.DescribeFailure(Context()));
        }

        [Fact]
        public void A_selection_that_is_no_longer_in_the_model_does_not_resolve()
        {
            // The failure that matters: a face was picked, the part was edited, and the
            // reference now points at nothing. Generating an empty toolpath here would
            // look like success.
            var height = new HeightSetting(HeightMode.FromSelection)
            {
                Reference = new GeometryRef
                {
                    PersistentId = "face-gone",
                    DisplayName = "Top face",
                },
            };

            var selections = new Dictionary<string, double> { ["face-1"] = 12.5 };

            Assert.False(height.TryResolve(Context(selections), out double _));
            Assert.Contains("Top face", height.DescribeFailure(Context(selections)));
        }

        [Fact]
        public void A_contour_height_measures_from_the_contour_plus_its_offset()
        {
            var height = new HeightSetting(HeightMode.FromContour, -1.5);

            Assert.True(height.TryResolve(Context().ForContour(12), out double z));
            Assert.Equal(10.5, z, 9);
        }

        [Fact]
        public void A_contour_height_does_not_resolve_without_a_contour()
        {
            // The operation as a whole has no one contour to measure from - the Heights
            // tab asks exactly that question, and has to get "no answer" rather than zero.
            var height = new HeightSetting(HeightMode.FromContour);

            Assert.False(height.TryResolve(Context(), out double _));
            Assert.Contains("contour", height.DescribeFailure(Context()));
        }

        [Fact]
        public void A_height_measured_from_the_top_needs_the_top_resolved_into_the_context()
        {
            var height = new HeightSetting(HeightMode.FromTop, 2);

            Assert.True(height.TryResolve(Context().WithTop(12), out double z));
            Assert.Equal(14, z, 9);

            // Only OperationHeights knows which setting is the top; resolved without it,
            // this fails rather than guessing.
            Assert.False(height.TryResolve(Context(), out double _));
            Assert.Contains("top height", height.DescribeFailure(Context()));
        }

        [Fact]
        public void A_height_measured_from_the_retract_needs_the_retract_resolved_into_the_context()
        {
            var height = new HeightSetting(HeightMode.FromRetract, 5);

            Assert.True(height.TryResolve(Context().WithRetract(35), out double z));
            Assert.Equal(40, z, 9);

            Assert.False(height.TryResolve(Context(), out double _));
            Assert.Contains("retract height", height.DescribeFailure(Context()));
        }

        [Fact]
        public void Resolving_the_top_and_the_retract_keeps_both()
        {
            HeightContext context = Context().WithTop(30).WithRetract(35);

            Assert.Equal(30, context.Top.Value, 9);
            Assert.Equal(35, context.Retract.Value, 9);
        }

        [Fact]
        public void Measuring_for_a_contour_forgets_a_top_resolved_for_another()
        {
            // A top measured from the contour moves with it, so carrying the old one over
            // would put the feed height where the last contour wanted it.
            HeightContext context = Context().WithTop(12).ForContour(7);

            Assert.Null(context.Top);
        }

        [Fact]
        public void Measuring_for_a_contour_keeps_everything_else_in_the_context()
        {
            var selections = new Dictionary<string, double> { ["face-1"] = 12.5 };

            HeightContext context = Context(selections).ForContour(7);

            Assert.Equal(30, context.StockTop, 9);
            Assert.Equal(-5, context.StockBottom, 9);
            Assert.Equal(25, context.ModelTop, 9);
            Assert.Equal(0, context.ModelBottom, 9);
            Assert.True(context.TryGetSelection("face-1", out double picked));
            Assert.Equal(12.5, picked, 9);
            Assert.Equal(7, context.ContourLevel.Value, 9);
        }

        [Fact]
        public void A_resolvable_height_describes_no_failure()
        {
            Assert.Null(new HeightSetting(HeightMode.FromStockTop, 5).DescribeFailure(Context()));
        }

        [Fact]
        public void Building_a_context_from_bounds_puts_the_top_at_the_top()
        {
            // Reading Min.Z as the top produces heights that look plausible and cut
            // through the table, so the factory exists to make that unspellable.
            var stock = new Bounds(new Vec3(0, 0, -5), new Vec3(100, 60, 30));
            var model = new Bounds(new Vec3(0, 0, 0), new Vec3(100, 60, 25));

            HeightContext context = HeightContext.From(stock, model);

            Assert.Equal(30, context.StockTop, 9);
            Assert.Equal(-5, context.StockBottom, 9);
            Assert.Equal(25, context.ModelTop, 9);
            Assert.Equal(0, context.ModelBottom, 9);
        }

        [Fact]
        public void Cloning_copies_the_reference_rather_than_sharing_it()
        {
            var original = new HeightSetting(HeightMode.FromSelection, 2)
            {
                Reference = new GeometryRef { PersistentId = "face-1", DisplayName = "Top face" },
            };

            HeightSetting copy = original.Clone();
            copy.Reference.DisplayName = "Something else";
            copy.Offset = 99;

            Assert.Equal("Top face", original.Reference.DisplayName);
            Assert.Equal(2, original.Offset, 9);
        }

        [Fact]
        public void Cloning_a_height_with_no_reference_does_not_invent_one()
        {
            Assert.Null(new HeightSetting(HeightMode.FromStockTop, 5).Clone().Reference);
        }
    }
}
