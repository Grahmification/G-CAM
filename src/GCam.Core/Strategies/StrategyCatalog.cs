using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Strategies.Contour2d;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// One strategy, as everything outside it needs to know it.
    /// </summary>
    public sealed class StrategyDescriptor
    {
        private readonly Func<StrategySettings> _newSettings;

        public StrategyDescriptor(StrategyId id, string displayName, Func<StrategySettings> newSettings)
        {
            if (id.IsEmpty)
            {
                throw new ArgumentException("A strategy needs an id.", nameof(id));
            }

            Id = id;
            DisplayName = displayName;
            _newSettings = newSettings ?? throw new ArgumentNullException(nameof(newSettings));
        }

        public StrategyId Id { get; }

        /// <summary>What to call it in the New Operation list. "2D Contour", not "contour2d".</summary>
        public string DisplayName { get; }

        /// <summary>A fresh settings object at its defaults.</summary>
        public StrategySettings CreateSettings() => _newSettings();
    }

    /// <summary>
    /// The strategies this build knows about.
    /// </summary>
    /// <remarks>
    /// The one place a strategy registers itself. New Operation reads it to offer a
    /// choice, persistence reads it to rebuild settings from a stored id, and the template
    /// importer reads it to decide whether it understands a file.
    ///
    /// An instance rather than a static table, so a test can register a fake strategy
    /// without reaching into global state, and so the composition root stays the place
    /// that decides what exists.
    /// </remarks>
    public sealed class StrategyCatalog
    {
        private readonly Dictionary<StrategyId, StrategyDescriptor> _byId =
            new Dictionary<StrategyId, StrategyDescriptor>();

        /// <summary>The strategies that are actually implemented, in display order.</summary>
        /// <remarks>
        /// Only 2D contour so far. Face, adaptive clearing and drilling are designed for -
        /// the base model was measured against all four - but none of them exists yet, and
        /// offering a strategy that cannot generate anything would be worse than not
        /// offering it.
        /// </remarks>
        public static StrategyCatalog CreateDefault()
        {
            var catalog = new StrategyCatalog();

            catalog.Register(new StrategyDescriptor(
                StrategyId.Contour2d, "2D Contour", () => new Contour2dSettings()));

            return catalog;
        }

        public void Register(StrategyDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            // Replacing rather than refusing: a test registering a fake over the real one
            // is the point of this being an instance.
            _byId[descriptor.Id] = descriptor;
        }

        public IReadOnlyList<StrategyDescriptor> All => _byId.Values.ToList();

        public bool TryGet(StrategyId id, out StrategyDescriptor descriptor) =>
            _byId.TryGetValue(id, out descriptor);

        public bool Knows(StrategyId id) => _byId.ContainsKey(id);

        /// <summary>
        /// A fresh settings object for a strategy.
        /// </summary>
        /// <exception cref="GCamUserException">
        /// When the id is not registered. That happens when a document or a template names
        /// a strategy this build does not have, which is a thing to tell the user about
        /// rather than a bug - so callers that must keep going, like reading a document
        /// full of operations, should ask <see cref="TryGet"/> instead.
        /// </exception>
        public StrategySettings CreateSettings(StrategyId id)
        {
            if (!TryGet(id, out StrategyDescriptor descriptor))
            {
                throw new GCamUserException(
                    $"This version of G-CAM does not have a '{id}' strategy.");
            }

            return descriptor.CreateSettings();
        }
    }
}
