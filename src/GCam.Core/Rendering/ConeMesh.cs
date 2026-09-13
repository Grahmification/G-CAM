using System;
using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// A cone as drawable triangles - the head of an arrow today, the business end of a
    /// chamfer mill or a spot drill later.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="BoxMesh"/> and in Core for the same reason: it is arithmetic
    /// with a right and a wrong answer, and Core is where a headless test reaches it.
    ///
    /// The cone is described by where its base and tip are rather than by an origin and a
    /// direction, because every caller so far knows both points and would otherwise have
    /// to take them apart and hand them back as a length and a unit vector.
    /// </remarks>
    public static class ConeMesh
    {
        /// <summary>
        /// Sides around the cone. Twelve is smooth enough for an arrowhead a few
        /// millimetres across and cheap enough not to think about; a caller drawing
        /// something larger can ask for more.
        /// </summary>
        public const int DefaultSegments = 12;

        /// <summary>
        /// The triangles of a closed cone - the curved side and the base cap - ready for
        /// <see cref="PrimitiveKind.Triangles"/>.
        /// </summary>
        /// <remarks>
        /// Capped rather than open, because an uncapped arrowhead is hollow and shows its
        /// inside the moment it is viewed from behind.
        ///
        /// Wound counter-clockwise seen from outside, matching <see cref="BoxMesh"/>.
        /// </remarks>
        /// <param name="baseCentre">Centre of the circular end.</param>
        /// <param name="apex">The point.</param>
        /// <param name="radius">Radius at the base.</param>
        /// <param name="segments">Sides around the circumference. At least three.</param>
        public static Vec3[] Triangles(
            Vec3 baseCentre, Vec3 apex, double radius, int segments = DefaultSegments)
        {
            if (segments < 3)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segments), segments, "A cone needs at least three sides.");
            }

            if (radius <= Precision.Epsilon)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius), radius, "A cone needs a radius greater than zero.");
            }

            Vec3 axis = (apex - baseCentre).Normalised();

            if (axis.Length < Precision.Epsilon)
            {
                throw new ArgumentException(
                    "A cone needs its apex away from its base.", nameof(apex));
            }

            Vec3[] ring = Ring(baseCentre, axis, radius, segments);

            var triangles = new List<Vec3>(segments * 6);

            for (int i = 0; i < segments; i++)
            {
                Vec3 here = ring[i];
                Vec3 next = ring[(i + 1) % segments];

                // Side, facing out and up towards the apex.
                triangles.Add(here);
                triangles.Add(next);
                triangles.Add(apex);

                // Base cap, facing away from the apex. Reversed relative to the side, so
                // that both come out counter-clockwise seen from their own outside.
                triangles.Add(baseCentre);
                triangles.Add(next);
                triangles.Add(here);
            }

            return triangles.ToArray();
        }

        /// <summary>
        /// The points of the circle at the base, swept counter-clockwise about the axis.
        /// </summary>
        private static Vec3[] Ring(Vec3 centre, Vec3 axis, double radius, int segments)
        {
            // Any two perpendicular directions will do - the cone is symmetric about its
            // axis, so where the seam falls is arbitrary. Taking the second from a cross
            // product rather than a second guess keeps the basis right-handed, which is
            // what makes the winding come out facing the way the comments above say.
            Vec3 u = axis.AnyPerpendicular();
            Vec3 v = axis.Cross(u);

            var ring = new Vec3[segments];

            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;

                ring[i] = centre
                          + (u * (radius * Math.Cos(angle)))
                          + (v * (radius * Math.Sin(angle)));
            }

            return ring;
        }
    }
}
