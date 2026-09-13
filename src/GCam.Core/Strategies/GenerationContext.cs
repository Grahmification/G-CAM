using System;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Tooling;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// Everything a strategy needs, already resolved.
    /// </summary>
    /// <remarks>
    /// **The point of this type is that a strategy touches no COM.** Heights are already
    /// numbers, the tool has already been looked up, the stock is already a box in the
    /// operation's frame. Whoever builds one of these has done all the work that needs
    /// SOLIDWORKS, so generation itself runs on a worker thread and a test can construct a
    /// context by hand.
    ///
    /// Millimetres, in the operation's frame.
    ///
    /// **Geometry is not here yet.** Selections resolve to real curves and faces only once
    /// `SolidWorks/Extraction` can walk a BRep, which is the slice after the first
    /// strategy. It arrives as another property on this type; nothing about the shape has
    /// to change to admit it.
    /// </remarks>
    public sealed class GenerationContext
    {
        public GenerationContext(
            Job job,
            Operation operation,
            Tool tool,
            ResolvedHeights heights,
            Bounds stock)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            Stock = stock;
        }

        public Job Job { get; }

        public Operation Operation { get; }

        /// <summary>The part tool this operation cuts with, already looked up.</summary>
        public Tool Tool { get; }

        /// <summary>The five heights as plain Z values.</summary>
        public ResolvedHeights Heights { get; }

        /// <summary>The stock box, in the operation's frame.</summary>
        public Bounds Stock { get; }

        /// <summary>This operation's own feeds and speeds, not the tool's defaults.</summary>
        public CuttingData Cutting => Operation.Cutting;

        /// <summary>Shorthand for the settings, cast to what the strategy expects.</summary>
        /// <exception cref="InvalidOperationException">
        /// The operation's settings are not the type this strategy works on, which means
        /// the catalogue has a strategy registered against the wrong settings.
        /// </exception>
        public TSettings SettingsAs<TSettings>() where TSettings : StrategySettings
        {
            if (!(Operation.Settings is TSettings settings))
            {
                throw new InvalidOperationException(
                    $"Operation '{Operation.Name}' carries {Operation.Settings.GetType().Name}, " +
                    $"which {typeof(TSettings).Name} cannot generate from.");
            }

            return settings;
        }
    }
}
