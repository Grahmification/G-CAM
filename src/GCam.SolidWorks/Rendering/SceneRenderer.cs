using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GCam.Core;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Rendering;
using GCam.SolidWorks.Rendering.Interop;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// Draws a <see cref="RenderScene"/> with OpenGL. The only class that knows both what
    /// Core wants drawn and how OpenGL draws it.
    /// </summary>
    /// <remarks>
    /// <b>Millimetres come in, metres go out.</b> Core works in millimetres; the matrices
    /// SOLIDWORKS has set up when this runs are in metres, in part coordinates. That
    /// conversion happens here, once per batch when the cache is built, and nowhere else
    /// - the edge the units rule in docs/architecture.md is about.
    ///
    /// <b>Vertices are cached as floats.</b> Converting millimetre doubles into metre
    /// floats every frame would be the most expensive thing in the draw once toolpaths
    /// are real, so the conversion is keyed on <see cref="RenderScene.Version"/> and only
    /// redone when the scene actually changes. Float rather than double because a GL_DOUBLE
    /// vertex array is a slow path on most drivers, and single precision over a metre-sized
    /// part resolves to well under a micron - far finer than anything a display needs.
    ///
    /// <b>Opaque before transparent.</b> Blending only composites correctly over what is
    /// already in the colour buffer, so anything solid has to be down first. Transparent
    /// batches then draw with depth testing on but depth writing off, which is what lets
    /// the model show through the stock box and keeps the box's own far wall from
    /// erasing its near one.
    /// </remarks>
    internal sealed class SceneRenderer
    {
        /// <summary>x, y, z.</summary>
        private const int ComponentsPerVertex = 3;

        private const byte GlFalse = 0;
        private const byte GlTrue = 1;

        /// <summary>Tightly packed, so OpenGL works the stride out itself.</summary>
        private const int PackedStride = 0;

        private readonly List<GlBatch> _batches = new List<GlBatch>();

        private int _cachedVersion = -1;

        /// <summary>
        /// Puts the scene on screen. Call only from inside BufferSwapNotify, where
        /// SOLIDWORKS' context is current.
        /// </summary>
        /// <returns>The number of batches drawn, for diagnostics.</returns>
        public int Draw(RenderScene scene)
        {
            if (scene == null)
            {
                return 0;
            }

            if (scene.Version != _cachedVersion)
            {
                Rebuild(scene);
            }

            if (_batches.Count == 0)
            {
                return 0;
            }

            using (new GlState())
            {
                BeginOverlay();

                foreach (GlBatch batch in _batches)
                {
                    DrawBatch(batch);
                }

                DrawArrows(scene);
            }

            return _batches.Count;
        }

        /// <summary>
        /// Draws the screen-sized arrows, which are built fresh every frame.
        /// </summary>
        /// <remarks>
        /// <b>Outside the cache, deliberately.</b> Everything else in a scene is fixed
        /// millimetres and is converted once per change; an arrow that holds its size on
        /// screen is a different shape at every zoom, so caching it against the scene's
        /// version would show yesterday's size. There are a handful of them and nine
        /// vertices each, which is what makes rebuilding per frame affordable where it
        /// would not be for a toolpath.
        ///
        /// Last, and always on top, because they are annotations about the model rather
        /// than things in it - and because the drawing-order rule means anything meant to
        /// sit above ordinary geometry has to be drawn after it.
        /// </remarks>
        private static void DrawArrows(RenderScene scene)
        {
            List<ScreenArrow> arrows = scene.Layers
                .Where(layer => layer.Visible)
                .SelectMany(layer => layer.Arrows)
                .ToList();

            if (arrows.Count == 0)
            {
                return;
            }

            ViewScale scale = ViewScale.Current();

            foreach (ScreenArrow arrow in arrows)
            {
                double millimetresPerPixel = scale.MillimetresPerPixel(arrow.Anchor);

                if (millimetresPerPixel <= 0)
                {
                    // Behind the camera, or a view so far out that a millimetre is not a
                    // pixel. Nothing useful to draw, and no reason to say so every frame.
                    continue;
                }

                DrawBatch(Convert(new RenderBatch(
                    PrimitiveKind.Triangles,
                    arrow.Triangles(millimetresPerPixel),
                    arrow.Colour,
                    alwaysOnTop: true)));
            }
        }

        /// <summary>
        /// Drops the cache. For a context that has gone away - the vertex data is still
        /// good, but anything OpenGL was holding is not.
        /// </summary>
        public void Reset()
        {
            _batches.Clear();
            _cachedVersion = -1;
        }

        /// <summary>
        /// The state every G-CAM overlay wants, set once rather than per batch.
        /// </summary>
        /// <remarks>
        /// Lighting off because these are flat annotations, not modelled surfaces, and a
        /// lit overlay takes its colour from the material state rather than from
        /// glColor. Texturing off for the same reason - SOLIDWORKS may well have a
        /// texture bound, and it would tint everything drawn here. Culling off so a
        /// translucent box shows both of its walls.
        ///
        /// Depth testing is not set here - it is per batch, because an always-on-top
        /// annotation turns it off and ordinary geometry needs it on.
        /// </remarks>
        private static void BeginOverlay()
        {
            Gl.Disable(Gl.GL_LIGHTING);
            Gl.Disable(Gl.GL_TEXTURE_2D);
            Gl.Disable(Gl.GL_CULL_FACE);

            Gl.Enable(Gl.GL_BLEND);
            Gl.BlendFunc(Gl.GL_SRC_ALPHA, Gl.GL_ONE_MINUS_SRC_ALPHA);

            Gl.Enable(Gl.GL_LINE_SMOOTH);
            Gl.Enable(Gl.GL_POINT_SMOOTH);

            Gl.EnableClientState(Gl.GL_VERTEX_ARRAY);
        }

        private static void DrawBatch(GlBatch batch)
        {
            Gl.Color4f(batch.Red, batch.Green, batch.Blue, batch.Alpha);

            // Two separate things, easily confused. Depth *testing* is whether the model
            // can hide this batch; the depth *mask* is whether this batch can hide what
            // comes after it.
            //
            // An annotation wants neither. Visible through the part, obviously - but also
            // leaving no trace in the depth buffer, because SOLIDWORKS renders Layer2
            // (active sketches, annotations, the reference triad) *after* this
            // notification and would then be depth-tested against a triad floating at
            // whatever depth it happened to have. Writing depth from something drawn
            // without depth testing corrupts the buffer for whoever reads it next.
            Gl.DepthMask(batch.Transparent || batch.AlwaysOnTop ? GlFalse : GlTrue);

            if (batch.AlwaysOnTop)
            {
                Gl.Disable(Gl.GL_DEPTH_TEST);
            }
            else
            {
                Gl.Enable(Gl.GL_DEPTH_TEST);
            }

            if (batch.Mode == Gl.GL_LINES || batch.Mode == Gl.GL_LINE_STRIP)
            {
                Gl.LineWidth(batch.LineWidth);
            }
            else if (batch.Mode == Gl.GL_POINTS)
            {
                Gl.PointSize(batch.PointSize);
            }

            // Pinned only for the pair of calls that need it. glVertexPointer hands
            // OpenGL a raw address which it reads when glDrawArrays runs, so the array
            // cannot be allowed to move in between; pinning for longer than that would
            // hold the heap open for nothing.
            GCHandle pin = GCHandle.Alloc(batch.Vertices, GCHandleType.Pinned);

            try
            {
                Gl.VertexPointer(
                    ComponentsPerVertex, Gl.GL_FLOAT, PackedStride, pin.AddrOfPinnedObject());

                Gl.DrawArrays(batch.Mode, 0, batch.VertexCount);
            }
            finally
            {
                pin.Free();
            }
        }

        /// <summary>
        /// Converts the scene into what OpenGL wants. Runs when the scene changes, not
        /// when it is drawn.
        /// </summary>
        private void Rebuild(RenderScene scene)
        {
            _batches.Clear();
            _cachedVersion = scene.Version;

            IEnumerable<RenderBatch> visible = scene.Layers
                .Where(layer => layer.Visible)
                .SelectMany(layer => layer.Batches);

            // False sorts before true, so this is: the scene proper, opaque first, then
            // the annotations that ignore depth. Both rules matter. Blending only
            // composites correctly over what is already in the colour buffer, so opaque
            // has to be down first; and an always-on-top batch drawn early would be
            // painted over by the ordinary geometry it is supposed to sit above.
            //
            // OrderBy and ThenBy are stable, so within each group the batches keep the
            // order their producer gave them - a producer still controls what covers
            // what, without being able to break the two rules it depends on.
            foreach (RenderBatch batch in visible
                .OrderBy(b => b.AlwaysOnTop)
                .ThenBy(b => b.Colour.IsTransparent))
            {
                _batches.Add(Convert(batch));
            }
        }

        private static GlBatch Convert(RenderBatch batch)
        {
            IReadOnlyList<Vec3> source = batch.Vertices;
            var vertices = new float[source.Count * ComponentsPerVertex];

            for (int i = 0; i < source.Count; i++)
            {
                Vec3 point = source[i];
                int at = i * ComponentsPerVertex;

                vertices[at] = (float)Units.MillimetresToMetres(point.X);
                vertices[at + 1] = (float)Units.MillimetresToMetres(point.Y);
                vertices[at + 2] = (float)Units.MillimetresToMetres(point.Z);
            }

            return new GlBatch
            {
                Mode = ModeOf(batch.Kind),
                Vertices = vertices,
                VertexCount = source.Count,
                Red = (float)batch.Colour.Red,
                Green = (float)batch.Colour.Green,
                Blue = (float)batch.Colour.Blue,
                Alpha = (float)batch.Colour.Alpha,
                Transparent = batch.Colour.IsTransparent,
                AlwaysOnTop = batch.AlwaysOnTop,
                LineWidth = (float)batch.LineWidth,
                PointSize = (float)batch.PointSize,
            };
        }

        private static uint ModeOf(PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Lines: return Gl.GL_LINES;
                case PrimitiveKind.LineStrip: return Gl.GL_LINE_STRIP;
                case PrimitiveKind.Points: return Gl.GL_POINTS;
                case PrimitiveKind.Triangles: return Gl.GL_TRIANGLES;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "No OpenGL primitive for this kind.");
            }
        }

        /// <summary>
        /// One batch as OpenGL wants it: metres, floats, and the colour already split
        /// into the four arguments glColor4f takes.
        /// </summary>
        private sealed class GlBatch
        {
            public uint Mode;

            public float[] Vertices;

            /// <summary>Vertices, not floats - <see cref="Vertices"/> holds three each.</summary>
            public int VertexCount;

            public float Red;

            public float Green;

            public float Blue;

            public float Alpha;

            public bool Transparent;

            public bool AlwaysOnTop;

            public float LineWidth;

            public float PointSize;
        }
    }
}
