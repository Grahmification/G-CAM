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
            }

            return _batches.Count;
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
        /// Depth testing stays on: the overlay belongs in the scene, behind whatever is
        /// in front of it.
        /// </remarks>
        private static void BeginOverlay()
        {
            Gl.Disable(Gl.GL_LIGHTING);
            Gl.Disable(Gl.GL_TEXTURE_2D);
            Gl.Disable(Gl.GL_CULL_FACE);

            Gl.Enable(Gl.GL_DEPTH_TEST);

            Gl.Enable(Gl.GL_BLEND);
            Gl.BlendFunc(Gl.GL_SRC_ALPHA, Gl.GL_ONE_MINUS_SRC_ALPHA);

            Gl.Enable(Gl.GL_LINE_SMOOTH);
            Gl.Enable(Gl.GL_POINT_SMOOTH);

            Gl.EnableClientState(Gl.GL_VERTEX_ARRAY);
        }

        private static void DrawBatch(GlBatch batch)
        {
            Gl.Color4f(batch.Red, batch.Green, batch.Blue, batch.Alpha);
            Gl.DepthMask(batch.Transparent ? GlFalse : GlTrue);

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

            // False sorts before true, so opaque batches come first. OrderBy is a stable
            // sort, so within each of those two groups the batches keep the order their
            // producer gave them - a producer can still control what covers what,
            // without being able to break the opaque-first rule it depends on.
            foreach (RenderBatch batch in visible.OrderBy(b => b.Colour.IsTransparent))
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

            public float LineWidth;

            public float PointSize;
        }
    }
}
