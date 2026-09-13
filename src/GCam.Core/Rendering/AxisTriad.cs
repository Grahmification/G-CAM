using System;
using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// Three coloured arrows showing where a coordinate system's origin is and which way
    /// its axes point.
    /// </summary>
    /// <remarks>
    /// <b>Red is X, green is Y, blue is Z.</b> The convention HSMWorks, Fusion,
    /// SOLIDWORKS' own reference triad and essentially every CAD package share, which is
    /// why the colours live here as named constants rather than being passed in: a triad
    /// drawn in G-CAM's own colours would be a triad nobody could read.
    ///
    /// The arrows are built <b>always on top</b>. A job origin very often sits inside the
    /// material - on the top face of the model, with stock above it - and an origin you
    /// cannot see is precisely the one you need to check. It is an annotation about the
    /// job rather than an object in the scene.
    ///
    /// Sizing is the caller's problem. This draws the triad at the length it is given;
    /// deciding that a triad should be some fraction of the stock is a judgement about
    /// stock, and it lives with the code that knows about stock.
    /// </remarks>
    public static class AxisTriad
    {
        public static readonly RenderColour XAxis = new RenderColour(0.90, 0.16, 0.16);
        public static readonly RenderColour YAxis = new RenderColour(0.16, 0.72, 0.22);
        public static readonly RenderColour ZAxis = new RenderColour(0.20, 0.42, 0.95);

        /// <summary>How much of an arrow's length is its head.</summary>
        private const double HeadLengthFraction = 0.22;

        /// <summary>The head's radius, again as a fraction of the whole arrow.</summary>
        private const double HeadRadiusFraction = 0.07;

        /// <summary>Shaft thickness in pixels. Heavy enough to read against a busy model.</summary>
        private const double ShaftWidth = 2.5;

        /// <summary>
        /// The batches for one triad: a shaft and a head per axis, six in all.
        /// </summary>
        /// <param name="frame">
        /// Where the coordinate system is and which way it faces, as a transform into the
        /// space the batches are drawn in. Its origin and axis directions are read out of
        /// it - the axes are renormalised, so a frame carrying a scale still produces
        /// arrows of the requested length.
        /// </param>
        /// <param name="length">Arrow length, in millimetres like everything in Core.</param>
        public static IReadOnlyList<RenderBatch> Build(Matrix4 frame, double length)
        {
            if (length <= Precision.Epsilon)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length), length, "A triad needs a length greater than zero.");
            }

            Vec3 origin = frame.Transform(Vec3.Zero);

            var batches = new List<RenderBatch>(6);

            Add(batches, origin, Direction(frame, new Vec3(1, 0, 0)), length, XAxis);
            Add(batches, origin, Direction(frame, new Vec3(0, 1, 0)), length, YAxis);
            Add(batches, origin, Direction(frame, new Vec3(0, 0, 1)), length, ZAxis);

            return batches;
        }

        /// <summary>
        /// One arrow: a line from the origin to the base of the head, and a cone from
        /// there to the tip.
        /// </summary>
        /// <remarks>
        /// The shaft stops where the head starts rather than running the whole length.
        /// Running it through would show the line poking out of the cone's tip at a
        /// glancing angle, since a line has no thickness in depth.
        /// </remarks>
        private static void Add(
            ICollection<RenderBatch> batches,
            Vec3 origin,
            Vec3 direction,
            double length,
            RenderColour colour)
        {
            if (direction.Length < Precision.Epsilon)
            {
                // A degenerate frame - an axis that collapsed to nothing. Skip the arrow
                // rather than throwing: two thirds of a triad still tells the user where
                // the origin is, which is more use than no triad at all.
                return;
            }

            double headLength = length * HeadLengthFraction;

            Vec3 tip = origin + (direction * length);
            Vec3 headBase = origin + (direction * (length - headLength));

            batches.Add(new RenderBatch(
                PrimitiveKind.Lines,
                new[] { origin, headBase },
                colour,
                lineWidth: ShaftWidth,
                alwaysOnTop: true));

            batches.Add(new RenderBatch(
                PrimitiveKind.Triangles,
                ConeMesh.Triangles(headBase, tip, length * HeadRadiusFraction),
                colour,
                alwaysOnTop: true));
        }

        private static Vec3 Direction(Matrix4 frame, Vec3 axis) =>
            frame.TransformDirection(axis).Normalised();
    }
}
