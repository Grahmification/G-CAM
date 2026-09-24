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
        {
            StockTop = stockTop;
            StockBottom = stockBottom;
            ModelTop = modelTop;
            ModelBottom = modelBottom;
            _selectionHeights = selectionHeights;
            ContourLevel = contourLevel;
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

        /// <summary>The same context, measured for one contour at the given Z.</summary>
        public HeightContext ForContour(double level) =>
            new HeightContext(StockTop, StockBottom, ModelTop, ModelBottom, _selectionHeights, level);

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
        /// For <see cref="HeightMode.FromSelection"/> and <see cref="HeightMode.FromContour"/>,
        /// which may have nothing to measure from and so are resolved by
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
                        nameof(mode), mode, "This mode needs a selection or a contour to resolve.");
            }
        }
    }
}
