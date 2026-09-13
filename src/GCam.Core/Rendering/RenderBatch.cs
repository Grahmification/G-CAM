using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// How a batch's vertices are joined up.
    /// </summary>
    /// <remarks>
    /// The four OpenGL modes that cover everything G-CAM will draw: shaded solids,
    /// disconnected segments, a continuous path, and markers. Deliberately not the whole
    /// OpenGL set - quads and polygons are deprecated and triangle strips are an
    /// optimisation nothing needs yet.
    /// </remarks>
    public enum PrimitiveKind
    {
        /// <summary>Every three vertices make a triangle.</summary>
        Triangles = 0,

        /// <summary>Every two vertices make a segment.</summary>
        Lines = 1,

        /// <summary>One continuous polyline through every vertex - a toolpath move.</summary>
        LineStrip = 2,

        /// <summary>One marker per vertex.</summary>
        Points = 3,
    }

    /// <summary>
    /// One drawable thing: a run of vertices, all the same colour, drawn in one go.
    /// </summary>
    /// <remarks>
    /// <b>Vertices are in millimetres, in the part's coordinate system.</b> Core works in
    /// millimetres everywhere and the renderer converts at the edge, exactly as the units
    /// rule in docs/architecture.md requires. A batch carries no transform: whoever
    /// builds one has already applied the job's coordinate system, which keeps the
    /// per-frame path free of matrix work and means a batch means the same thing however
    /// it is drawn.
    ///
    /// Batching rather than a scene graph of individual shapes is the whole point. A
    /// contour toolpath is hundreds of thousands of segments; as one batch that is a
    /// single draw call, and as individual objects it is a frame-rate problem. The stock
    /// box uses the same shape as the toolpaths will.
    ///
    /// Immutable, because the renderer caches a converted copy of the vertices and
    /// detects changes by the scene's version rather than by watching every batch.
    /// Replace a batch to change it.
    /// </remarks>
    public sealed class RenderBatch
    {
        /// <summary>Line width in pixels when nothing else is asked for.</summary>
        public const double DefaultLineWidth = 1.0;

        /// <summary>Point size in pixels when nothing else is asked for.</summary>
        public const double DefaultPointSize = 4.0;

        public RenderBatch(
            PrimitiveKind kind,
            IEnumerable<Vec3> vertices,
            RenderColour colour,
            double lineWidth = DefaultLineWidth,
            double pointSize = DefaultPointSize)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            Kind = kind;
            Vertices = vertices as IReadOnlyList<Vec3> ?? vertices.ToArray();
            Colour = colour;
            LineWidth = lineWidth;
            PointSize = pointSize;
        }

        public PrimitiveKind Kind { get; }

        /// <summary>Millimetres, in part coordinates. See the remarks on this class.</summary>
        public IReadOnlyList<Vec3> Vertices { get; }

        public RenderColour Colour { get; }

        /// <summary>Pixels. Only read for <see cref="PrimitiveKind.Lines"/> and line strips.</summary>
        public double LineWidth { get; }

        /// <summary>Pixels. Only read for <see cref="PrimitiveKind.Points"/>.</summary>
        public double PointSize { get; }

        /// <summary>True when there is nothing to draw, or not enough to form a primitive.</summary>
        public bool IsEmpty => Vertices.Count < VerticesPerPrimitive(Kind);

        private static int VerticesPerPrimitive(PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Triangles: return 3;
                case PrimitiveKind.Lines: return 2;
                case PrimitiveKind.LineStrip: return 2;
                default: return 1;
            }
        }

        public override string ToString() => $"{Kind} x{Vertices.Count} {Colour}";
    }
}
