using System;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// Turns a box into something drawable - the corners, the twelve triangles that fill
    /// it, and the twelve edges that outline it.
    /// </summary>
    /// <remarks>
    /// In Core rather than beside the renderer because it is arithmetic with a right and
    /// a wrong answer, and Core is where a headless test can reach it. The rule from
    /// docs/architecture.md that put ToolSearch in Core puts this here too.
    ///
    /// The stock box is the first caller. A machining extent, a Z-map cell and a tool
    /// holder's envelope are all boxes as well, which is why this is not called
    /// StockMesh.
    /// </remarks>
    public static class BoxMesh
    {
        /// <summary>A box has eight corners.</summary>
        public const int CornerCount = 8;

        /// <summary>
        /// The eight corners, optionally carried into another coordinate system.
        /// </summary>
        /// <remarks>
        /// <b>Corner order is part of this type's contract</b> - the other methods index
        /// into what this returns, and so may a caller. Bit 0 is X, bit 1 is Y, bit 2 is
        /// Z; a set bit means the maximum on that axis. So 0 is (min, min, min), 1 is
        /// (max, min, min), and 7 is (max, max, max).
        ///
        /// <paramref name="toWorld"/> is applied per corner, which is what lets a box
        /// defined in a job's coordinate system be drawn in the part's. Pass
        /// <see cref="Matrix4.Identity"/> when the box is already where it belongs. The
        /// result is no longer axis-aligned once a rotating transform is involved, which
        /// is exactly the point - eight transformed corners still describe the box
        /// correctly, where a transformed <see cref="Bounds"/> would not.
        /// </remarks>
        public static Vec3[] Corners(Bounds box, Matrix4 toWorld)
        {
            var corners = new Vec3[CornerCount];

            for (int i = 0; i < CornerCount; i++)
            {
                var corner = new Vec3(
                    (i & 1) == 0 ? box.Min.X : box.Max.X,
                    (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                    (i & 4) == 0 ? box.Min.Z : box.Max.Z);

                corners[i] = toWorld.Transform(corner);
            }

            return corners;
        }

        /// <summary>
        /// The thirty-six vertices of the twelve triangles filling the box, ready for
        /// <see cref="PrimitiveKind.Triangles"/>.
        /// </summary>
        /// <remarks>
        /// Wound counter-clockwise seen from outside, so back-face culling and lighting
        /// would both behave if anything ever turned them on. Nothing does today - a
        /// translucent box is drawn with culling off so both walls show - but winding
        /// that is wrong only becomes visible long after it is written.
        /// </remarks>
        public static Vec3[] Triangles(Vec3[] corners)
        {
            Require(corners);

            // Each face as two triangles, listed in the corner order above.
            int[] indices =
            {
                0, 2, 3,  0, 3, 1,   // -Z, seen from below
                4, 5, 7,  4, 7, 6,   // +Z
                0, 1, 5,  0, 5, 4,   // -Y
                2, 6, 7,  2, 7, 3,   // +Y
                0, 4, 6,  0, 6, 2,   // -X
                1, 3, 7,  1, 7, 5,   // +X
            };

            return Gather(corners, indices);
        }

        /// <summary>
        /// The twenty-four vertices of the twelve edges outlining the box, ready for
        /// <see cref="PrimitiveKind.Lines"/>.
        /// </summary>
        public static Vec3[] Edges(Vec3[] corners)
        {
            Require(corners);

            int[] indices =
            {
                0, 1,  2, 3,  4, 5,  6, 7,   // along X
                0, 2,  1, 3,  4, 6,  5, 7,   // along Y
                0, 4,  1, 5,  2, 6,  3, 7,   // along Z
            };

            return Gather(corners, indices);
        }

        private static Vec3[] Gather(Vec3[] corners, int[] indices)
        {
            var vertices = new Vec3[indices.Length];

            for (int i = 0; i < indices.Length; i++)
            {
                vertices[i] = corners[indices[i]];
            }

            return vertices;
        }

        private static void Require(Vec3[] corners)
        {
            if (corners == null)
            {
                throw new ArgumentNullException(nameof(corners));
            }

            if (corners.Length != CornerCount)
            {
                throw new ArgumentException(
                    $"A box has {CornerCount} corners, not {corners.Length}. Use {nameof(Corners)} to build them.",
                    nameof(corners));
            }
        }
    }
}
