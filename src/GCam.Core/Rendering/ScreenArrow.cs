using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Rendering
{
    /// <summary>
    /// A flat arrow anchored in the model but sized on the screen: it stays the same size
    /// however far the view is zoomed in or out.
    /// </summary>
    /// <remarks>
    /// <b>The one thing in the scene that is not fixed geometry.</b> Everything else Core
    /// hands the renderer is millimetres that mean the same at any zoom, which is what
    /// lets the renderer convert once and cache. An arrow saying which side of an edge
    /// will be cut cannot work that way: sized in millimetres it is a speck on a plate and
    /// bigger than the part on a boss. So this carries the shape in <b>pixels</b> and the
    /// renderer asks for the vertices each frame, having measured what a pixel is worth
    /// where the arrow stands.
    ///
    /// Drawn in the plane the two directions span, not turned to face the camera. An arrow
    /// that swivelled would read as an annotation floating in front of the part; one lying
    /// in the cutting plane reads as belonging to the edge it is about, which is the whole
    /// point of it. The cost is that it foreshortens when the view is oblique - which is
    /// honest, and recoverable by looking down.
    ///
    /// Immutable for the same reason <see cref="RenderBatch"/> is: the scene's version is
    /// what tells a renderer something changed.
    /// </remarks>
    public sealed class ScreenArrow
    {
        /// <summary>How far off the anchor the arrow floats, pixels.</summary>
        /// <remarks>
        /// Clear of the edge it annotates - which SOLIDWORKS draws highlighted and
        /// thickened while it is selected - without drifting so far that which edge it
        /// belongs to becomes a guess.
        /// </remarks>
        public const double GapPixels = 18;

        /// <summary>Tail to tip, pixels.</summary>
        public const double LengthPixels = 46;

        public const double HeadLengthPixels = 18;

        public const double HeadWidthPixels = 16;

        public const double ShaftWidthPixels = 5;

        public ScreenArrow(Vec3 anchor, Vec3 travel, Vec3 side, RenderColour colour)
        {
            Anchor = anchor;
            Travel = travel;
            Side = side;
            Colour = colour;
        }

        /// <summary>Millimetres, in part coordinates, like every other vertex in a scene.</summary>
        public Vec3 Anchor { get; }

        /// <summary>Unit vector the arrow points along.</summary>
        public Vec3 Travel { get; }

        /// <summary>Unit vector the arrow is offset along, away from the anchor.</summary>
        public Vec3 Side { get; }

        public RenderColour Colour { get; }

        /// <summary>
        /// The arrow as a triangle list, millimetres, for a view at this scale.
        /// </summary>
        /// <param name="millimetresPerPixel">
        /// What one screen pixel is worth in model millimetres where the anchor stands.
        /// Under a perspective projection that varies with depth, so it is measured at the
        /// anchor rather than once for the view.
        /// </param>
        public IReadOnlyList<Vec3> Triangles(double millimetresPerPixel)
        {
            Vec3 along = Travel * millimetresPerPixel;
            Vec3 across = Side * millimetresPerPixel;

            Vec3 centre = Anchor + (across * GapPixels);
            Vec3 tail = centre - (along * (LengthPixels / 2));
            Vec3 tip = centre + (along * (LengthPixels / 2));
            Vec3 neck = tip - (along * HeadLengthPixels);

            Vec3 shaft = across * (ShaftWidthPixels / 2);
            Vec3 barb = across * (HeadWidthPixels / 2);

            return new[]
            {
                // Shaft, as two triangles.
                tail - shaft, tail + shaft, neck + shaft,
                tail - shaft, neck + shaft, neck - shaft,

                // Head.
                neck - barb, neck + barb, tip,
            };
        }

        public override string ToString() => $"arrow at {Anchor} along {Travel}";
    }
}
