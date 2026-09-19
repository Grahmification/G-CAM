using System.Linq;
using GCam.Core.Model;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Strategies.Shared;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class Contour2dSettingsTests
    {
        private static Contour2dSettings Usable()
        {
            var settings = new Contour2dSettings();
            settings.Contours.Add(new ContourSelection(new GeometryRef
            {
                PersistentId = "edge-1",
                Kind = GeometryRefKind.Edge,
                DisplayName = "Edge1",
            }));
            return settings;
        }

        [Fact]
        public void It_knows_which_strategy_it_is()
        {
            Assert.Equal(StrategyId.Contour2d, new Contour2dSettings().Strategy);
        }

        [Fact]
        public void It_does_not_depend_on_what_earlier_operations_left()
        {
            // Rest machining is an adaptive-clearing feature. If this ever becomes true,
            // reordering operations starts invalidating this one.
            Assert.False(new Contour2dSettings().DependsOnPrecedingStock);
        }

        [Fact]
        public void A_contour_with_nothing_selected_is_reported()
        {
            string problem = Assert.Single(new Contour2dSettings().Validate());

            Assert.Contains("at least one contour", problem);
        }

        [Fact]
        public void An_empty_selection_among_real_ones_is_reported()
        {
            Contour2dSettings settings = Usable();
            settings.Contours.Add(new ContourSelection(new GeometryRef()));

            Assert.Contains(settings.Validate(), p => p.Contains("empty"));
        }

        [Fact]
        public void A_usable_contour_has_no_problems()
        {
            Assert.Empty(Usable().Validate());
        }

        [Fact]
        public void Negative_stock_to_leave_is_allowed_on_both_axes()
        {
            // It cuts past the profile rather than short of it - how an undersize cutter
            // is taken out. Refused until 2026-09-19.
            Contour2dSettings settings = Usable();
            settings.StockToLeave = -0.2;
            settings.VerticalStockToLeave = -0.2;

            Assert.Empty(settings.Validate());
        }

        [Fact]
        public void Multiple_depths_with_no_stepdown_is_reported()
        {
            Contour2dSettings settings = Usable();
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 0;

            Assert.Contains(settings.Validate(), p => p.Contains("Maximum stepdown"));
        }

        [Fact]
        public void A_disabled_stepdown_of_zero_is_not_a_problem()
        {
            // Off means the value is not read, so it has no business failing validation.
            Contour2dSettings settings = Usable();
            settings.MultipleDepths.MaximumStepdown = 0;

            Assert.Empty(settings.Validate());
        }

        [Fact]
        public void The_lead_out_is_only_validated_when_it_is_its_own()
        {
            Contour2dSettings settings = Usable();
            settings.LeadOut.Radius = -5;

            // Matching the lead-in, so the bad value is not in use.
            Assert.Empty(settings.Validate());

            settings.LeadOutMatchesLeadIn = false;
            Assert.Contains(settings.Validate(), p => p.Contains("Lead-out radius"));
        }

        [Fact]
        public void The_effective_lead_out_follows_the_lead_in_until_it_is_unlinked()
        {
            var settings = new Contour2dSettings();
            settings.LeadIn.Radius = 7;
            settings.LeadOut.Radius = 3;

            Assert.Equal(7, settings.EffectiveLeadOut.Radius, 9);

            settings.LeadOutMatchesLeadIn = false;
            Assert.Equal(3, settings.EffectiveLeadOut.Radius, 9);
        }

        [Fact]
        public void Unlinking_the_lead_out_does_not_lose_what_was_typed_into_it()
        {
            var settings = new Contour2dSettings();
            settings.LeadOut.Radius = 3;
            settings.LeadOutMatchesLeadIn = true;

            Assert.Equal(3, settings.LeadOut.Radius, 9);
        }

        [Fact]
        public void A_sweep_outside_a_full_circle_is_reported()
        {
            Contour2dSettings settings = Usable();
            settings.LeadIn.Sweep = 400;

            Assert.Contains(settings.Validate(), p => p.Contains("sweep"));
        }

        [Fact]
        public void Cloning_copies_the_contours_the_groups_and_nothing_shared()
        {
            Contour2dSettings original = Usable();
            original.Direction = CutDirection.Conventional;
            original.StockToLeave = 0.2;
            original.MultipleDepths.MaximumStepdown = 3;
            original.LeadIn.Radius = 7;

            var copy = (Contour2dSettings)original.Clone();
            copy.Contours[0].Entity.DisplayName = "Changed";
            copy.Contours[0].PropagateTangent = false;
            copy.Contours.Add(new ContourSelection(new GeometryRef { PersistentId = "edge-2" }));
            copy.MultipleDepths.MaximumStepdown = 99;
            copy.LeadIn.Radius = 99;

            Assert.Equal("Edge1", original.Contours.Single().Entity.DisplayName);
            Assert.True(original.Contours.Single().PropagateTangent);
            Assert.Equal(3, original.MultipleDepths.MaximumStepdown, 9);
            Assert.Equal(7, original.LeadIn.Radius, 9);
            Assert.Equal(CutDirection.Conventional, copy.Direction);
            Assert.Equal(0.2, copy.StockToLeave, 9);
        }
    }
}
