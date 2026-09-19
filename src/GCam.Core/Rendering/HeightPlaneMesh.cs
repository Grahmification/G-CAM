using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// Turns one machining height into a plane to look at: a rectangle at that Z, outlined
    /// always and filled when it is the one being edited.
    /// </summary>
    /// <remarks>
    /// In Core for the reason <see cref="BoxMesh"/> is - it is arithmetic with a right
    /// answer, and this is where a headless test can reach it.
    ///
    /// <b>The outline ignores depth and the fill does not.</b> They are answering two
    /// different questions. The outline says where the plane is and has to be readable
    /// whatever it is behind, and at the model's own top or bottom face it would otherwise
    /// z-fight with the face it sits on - Bottom defaults to exactly there. The fill says
    /// which plane is being edited, and it is worth more for being occluded: a plane under
    /// the part, seen from above, should show as an outline sticking out past the
    /// silhouette rather than as a wash over the model it is beneath.
    /// </remarks>
    public static class HeightPlaneMesh
    {
        /// <summary>Pixels. Heavier than a toolpath, because it is a datum, not a path.</summary>
        public const double OutlineWidth = 2.0;

        /// <summary>
        /// How solid the fill is. Enough to tell which plane is lit, little enough to read
        /// the model through it.
        /// </summary>
        public const double FillAlpha = 0.18;

        /// <summary>
        /// How far past the extent the plane reaches, as a fraction of its larger side.
        /// </summary>
        /// <remarks>
        /// A fraction rather than a fixed distance so it looks the same on a 20mm part and
        /// a metre-long one - the same reasoning as the origin triad's proportional arms.
        /// </remarks>
        public const double MarginFraction = 0.06;

        /// <summary>The margin never falls below this, millimetres.</summary>
        /// <remarks>On a very small part a fraction rounds to nothing to look at.</remarks>
        public const double MinimumMargin = 1.0;

        /// <summary>
        /// The plane at <paramref name="z"/>, sized to cover <paramref name="extent"/> in
        /// XY with a margin round it.
        /// </summary>
        /// <param name="extent">
        /// What the plane has to reach past, in the same frame as <paramref name="z"/>.
        /// Only X and Y are read.
        /// </param>
        /// <param name="toPart">
        /// The job's frame to the part's, applied per corner - the same contract as
        /// <see cref="BoxMesh.Corners"/>, and required for the same reason.
        /// </param>
        public static IEnumerable<RenderBatch> Build(
            Bounds extent, double z, Matrix4 toPart, RenderColour colour, bool filled)
        {
            var batches = new List<RenderBatch>();

            if (extent.IsEmpty)
            {
                return batches;
            }

            Vec3[] corners = Corners(extent, z, toPart);

            if (filled)
            {
                batches.Add(new RenderBatch(
                    PrimitiveKind.Triangles,
                    new[]
                    {
                        corners[0], corners[1], corners[2],
                        corners[0], corners[2], corners[3],
                    },
                    colour.WithAlpha(FillAlpha)));
            }

            // Five points, not four: a line strip does not close itself.
            batches.Add(new RenderBatch(
                PrimitiveKind.LineStrip,
                new[] { corners[0], corners[1], corners[2], corners[3], corners[0] },
                colour,
                lineWidth: OutlineWidth,
                alwaysOnTop: true));

            return batches;
        }

        /// <summary>The four corners, anticlockwise from (min, min).</summary>
        private static Vec3[] Corners(Bounds extent, double z, Matrix4 toPart)
        {
            Vec3 size = extent.Size;
            double margin = System.Math.Max(
                MinimumMargin, System.Math.Max(size.X, size.Y) * MarginFraction);

            double minX = extent.Min.X - margin;
            double maxX = extent.Max.X + margin;
            double minY = extent.Min.Y - margin;
            double maxY = extent.Max.Y + margin;

            return new[]
            {
                toPart.Transform(new Vec3(minX, minY, z)),
                toPart.Transform(new Vec3(maxX, minY, z)),
                toPart.Transform(new Vec3(maxX, maxY, z)),
                toPart.Transform(new Vec3(minX, maxY, z)),
            };
        }
    }
}
