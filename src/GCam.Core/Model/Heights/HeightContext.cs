using System;
using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// The Z values a <see cref="HeightMode"/> is measured from, resolved for one
    /// operation.
    /// </summary>
    /// <remarks>
    /// Every Z is in millimetres in the operation's frame, which is what makes
    /// <see cref="HeightMode.FromJobOrigin"/> simply zero.
    ///
    /// Selections are resolved to plain numbers *before* this is built, by
    /// GCam.SolidWorks, which is the only project that can turn a persistent reference
    /// into geometry. Core then does pure arithmetic over doubles - no delegates, no
    /// callbacks back across the boundary, and every mode testable headlessly.
    ///
    /// Immutable on purpose. Resolving the same heights twice against the same context
    /// has to give the same answer, or "stale" stops meaning anything.
    /// </remarks>
    public sealed class HeightContext
    {
        private readonly IReadOnlyDictionary<string, double> _selectionHeights;

        public HeightContext(
            double stockTop,
            double stockBottom,
            double modelTop,
            double modelBottom,
            IReadOnlyDictionary<string, double> selectionHeights = null,
            double? contourLevel = null)
            : this(stockTop, stockBottom, modelTop, modelBottom, selectionHeights, contourLevel, null, null)
        {
        }

        private HeightContext(
            double stockTop,
            double stockBottom,
            double modelTop,
            double modelBottom,
            IReadOnlyDictionary<string, double> selectionHeights,
            double? contourLevel,
            double? top,
            double? retract)
        {
            StockTop = stockTop;
            StockBottom = stockBottom;
            ModelTop = modelTop;
            ModelBottom = modelBottom;
            _selectionHeights = selectionHeights;
            ContourLevel = contourLevel;
            Top = top;
            Retract = retract;
        }

        public double StockTop { get; }

        public double StockBottom { get; }

        public double ModelTop { get; }

        public double ModelBottom { get; }

        /// <summary>
        /// The Z of the one contour these heights are being resolved for, or null when
        /// they are being resolved for the operation as a whole.
        /// </summary>
        /// <remarks>
        /// Null is the ordinary case, not a fault: the Heights tab and anything else that
        /// asks about the operation rather than one of its chains has no single contour to
        /// name, and <see cref="HeightMode.FromContour"/> then simply does not resolve.
        /// </remarks>
        public double? ContourLevel { get; }

        /// <summary>
        /// The operation's resolved top height, for <see cref="HeightMode.FromTop"/>; null
        /// until <see cref="OperationHeights"/> has resolved it.
        /// </summary>
        /// <remarks>
        /// Set by <see cref="OperationHeights"/> rather than by whoever builds the context,
        /// because it is not an extent of anything - it is the answer to another height,
        /// and only the owner of all five knows which one that is.
        /// </remarks>
        public double? Top { get; }

        /// <summary>
        /// The operation's resolved retract height, for <see cref="HeightMode.FromRetract"/>;
        /// null until <see cref="OperationHeights"/> has resolved it. Set there for the same
        /// reason as <see cref="Top"/>.
        /// </summary>
        public double? Retract { get; }

        /// <summary>The same context, measured for one contour at the given Z.</summary>
        /// <remarks>
        /// Drops any resolved top and retract: a top measured from the contour moves with
        /// it, so the old answer would be for the wrong contour. The retract cannot follow
        /// a contour, but it is re-resolved with the top rather than trusted separately.
        /// </remarks>
        public HeightContext ForContour(double level) =>
            new HeightContext(
                StockTop, StockBottom, ModelTop, ModelBottom, _selectionHeights, level, null, null);

        /// <summary>The same context, with the operation's top height resolved.</summary>
        public HeightContext WithTop(double top) =>
            new HeightContext(
                StockTop, StockBottom, ModelTop, ModelBottom, _selectionHeights, ContourLevel, top, Retract);

        /// <summary>The same context, with the operation's retract height resolved.</summary>
        public HeightContext WithRetract(double retract) =>
            new HeightContext(
                StockTop, StockBottom, ModelTop, ModelBottom, _selectionHeights, ContourLevel, Top, retract);

        /// <summary>
        /// Builds a context from the stock and model extents, already expressed in the
        /// operation's frame.
        /// </summary>
        /// <remarks>
        /// Exists so callers cannot pick the wrong end of a <see cref="Bounds"/>. Reading
        /// Min.Z as the top is a mistake that produces heights which look plausible and
        /// cut through the table.
        /// </remarks>
        public static HeightContext From(
            Bounds stock,
            Bounds model,
            IReadOnlyDictionary<string, double> selectionHeights = null)
        {
            return new HeightContext(
                stock.Max.Z, stock.Min.Z,
                model.Max.Z, model.Min.Z,
                selectionHeights);
        }

        /// <summary>
        /// The Z of a picked entity. False when nothing was picked, or when the pick no
        /// longer resolves to anything in the model.
        /// </summary>
        public bool TryGetSelection(string persistentId, out double z)
        {
            z = 0;

            if (string.IsNullOrWhiteSpace(persistentId) || _selectionHeights == null)
            {
                return false;
            }

            return _selectionHeights.TryGetValue(persistentId, out z);
        }

        /// <summary>The datum for a mode that does not need a selection.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// For <see cref="HeightMode.FromSelection"/>, <see cref="HeightMode.FromContour"/>,
        /// <see cref="HeightMode.FromTop"/> and <see cref="HeightMode.FromRetract"/>, which
        /// may have nothing to measure from and so are resolved by
        /// <see cref="HeightSetting"/> rather than here.
        /// </exception>
        public double Datum(HeightMode mode)
        {
            switch (mode)
            {
                case HeightMode.FromStockTop: return StockTop;
                case HeightMode.FromStockBottom: return StockBottom;
                case HeightMode.FromModelTop: return ModelTop;
                case HeightMode.FromModelBottom: return ModelBottom;
                case HeightMode.FromJobOrigin: return 0;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mode), mode, "This mode needs a selection, a contour or another height to resolve.");
            }
        }
    }
}
