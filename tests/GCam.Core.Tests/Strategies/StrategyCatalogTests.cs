using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Strategies
{
    public class StrategyCatalogTests
    {
        private sealed class FakeSettings : StrategySettings
        {
            public override StrategyId Strategy => new StrategyId("fake");

            public override StrategySettings Clone() => new FakeSettings();
        }

        [Fact]
        public void The_default_catalogue_offers_the_strategies_that_actually_work()
        {
            StrategyCatalog catalog = StrategyCatalog.CreateDefault();

            Assert.True(catalog.Knows(StrategyId.Contour2d));

            // Designed for, not built. Offering a strategy that cannot generate anything
            // would be worse than not offering it.
            Assert.False(catalog.Knows(StrategyId.Drill));
            Assert.False(catalog.Knows(StrategyId.Face));
            Assert.False(catalog.Knows(StrategyId.Adaptive2d));
        }

        [Fact]
        public void Creating_settings_gives_a_fresh_object_each_time()
        {
            StrategyCatalog catalog = StrategyCatalog.CreateDefault();

            StrategySettings first = catalog.CreateSettings(StrategyId.Contour2d);
            StrategySettings second = catalog.CreateSettings(StrategyId.Contour2d);

            Assert.IsType<Contour2dSettings>(first);
            Assert.False(ReferenceEquals(first, second));
        }

        [Fact]
        public void An_unknown_strategy_is_a_user_error_not_a_crash()
        {
            // This is what a document written by a newer build looks like, so the message
            // has to be readable rather than a stack trace.
            StrategyCatalog catalog = StrategyCatalog.CreateDefault();

            var error = Assert.Throws<GCamUserException>(
                () => catalog.CreateSettings(new StrategyId("swarf")));

            Assert.Contains("swarf", error.Message);
        }

        [Fact]
        public void Callers_that_must_keep_going_can_ask_instead_of_catching()
        {
            // Reading a document full of operations cannot stop at the first strategy it
            // does not recognise.
            StrategyCatalog catalog = StrategyCatalog.CreateDefault();

            Assert.False(catalog.TryGet(new StrategyId("swarf"), out StrategyDescriptor missing));
            Assert.Null(missing);

            Assert.True(catalog.TryGet(StrategyId.Contour2d, out StrategyDescriptor found));
            Assert.Equal("2D Contour", found.DisplayName);
        }

        [Fact]
        public void A_test_can_register_its_own_strategy_without_touching_global_state()
        {
            var catalog = new StrategyCatalog();
            catalog.Register(new StrategyDescriptor(
                new StrategyId("fake"), "Fake", () => new FakeSettings()));

            Assert.IsType<FakeSettings>(catalog.CreateSettings(new StrategyId("fake")));

            // The default catalogue is unaffected, which is the point of it being an
            // instance rather than a static table.
            Assert.False(StrategyCatalog.CreateDefault().Knows(new StrategyId("fake")));
        }

        [Fact]
        public void Registering_the_same_id_twice_replaces_it()
        {
            var catalog = new StrategyCatalog();
            catalog.Register(new StrategyDescriptor(StrategyId.Contour2d, "Real", () => new Contour2dSettings()));
            catalog.Register(new StrategyDescriptor(StrategyId.Contour2d, "Fake", () => new FakeSettings()));

            catalog.TryGet(StrategyId.Contour2d, out StrategyDescriptor descriptor);

            Assert.Equal("Fake", descriptor.DisplayName);
            Assert.Single(catalog.All);
        }

        [Fact]
        public void A_descriptor_needs_an_id_and_a_factory()
        {
            Assert.Throws<ArgumentException>(
                () => new StrategyDescriptor(default, "No id", () => new FakeSettings()));

            Assert.Throws<ArgumentNullException>(
                () => new StrategyDescriptor(StrategyId.Contour2d, "No factory", null));
        }

        [Fact]
        public void Everything_registered_is_listed()
        {
            IReadOnlyList<StrategyDescriptor> all = StrategyCatalog.CreateDefault().All;

            Assert.Equal(new[] { StrategyId.Contour2d }, all.Select(d => d.Id));
        }
    }
}
