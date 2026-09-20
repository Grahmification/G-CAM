using System;
using System.Collections.Generic;
using System.Linq;
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
            Bounds stock,
            IReadOnlyList<ResolvedContour> contours = null)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            Stock = stock;
            Contours = contours ?? new ResolvedContour[0];
        }

        /// <summary>
        /// Convenience for plain curves picked with no modifiers - which is every contour
        /// in a test that does not care about sides.
        /// </summary>
        public GenerationContext(
            Job job,
            Operation operation,
            Tool tool,
            ResolvedHeights heights,
            Bounds stock,
            IReadOnlyList<Polyline> contours)
            : this(
                job,
                operation,
                tool,
                heights,
                stock,
                contours?.Select(c => new ResolvedContour(c)).ToList())
        {
        }

        public Job Job { get; }

        public Operation Operation { get; }

        /// <summary>
        /// What the user should read about a run that nonetheless produced a toolpath.
        /// </summary>
        /// <remarks>
        /// The one thing a strategy says other than the path itself, and the channel is
        /// here because there is nowhere else for it: a strategy that cannot proceed at
        /// all throws, and everything else it knows would otherwise be lost. The queue
        /// puts these on the operation as <see cref="OperationState.Warning"/> - there is
        /// a path, and something about it is worth knowing.
        ///
        /// A strategy stays deterministic: the same context in gives the same path and the
        /// same warnings out.
        /// </remarks>
        public IList<string> Warnings { get; } = new List<string>();

        /// <summary>The part tool this operation cuts with, already looked up.</summary>
        public Tool Tool { get; }

        /// <summary>The five heights as plain Z values.</summary>
        public ResolvedHeights Heights { get; }

        /// <summary>The stock box, in the operation's frame.</summary>
        public Bounds Stock { get; }

        /// <summary>
        /// The operation's selected contours, already resolved to chains of points in the
        /// operation's frame - open or closed - each with the intent it was picked with.
        /// </summary>
        /// <remarks>
        /// Tessellated by `SolidWorks/Extraction` from whatever SOLIDWORKS curves the
        /// selection turned out to be, to the operation's tolerance, and transformed into
        /// the operation's frame - so a strategy never sees a spline, a metre, or a
        /// rotated coordinate system.
        ///
        /// **Open chains are contours too.** They were dropped here until 2026-09-13, on
        /// the grounds that the offsetter could not handle them; it can now, so a partial
        /// profile is a thing to cut rather than a warning. See
        /// <see cref="Geometry.Offset.IContourOffsetter.OffsetOpen"/>.
        ///
        /// Empty for a strategy that does not select contours, and for one that does but
        /// whose selections no longer resolve. The strategy decides which of those is an
        /// error.
        /// </remarks>
        public IReadOnlyList<ResolvedContour> Contours { get; }

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
