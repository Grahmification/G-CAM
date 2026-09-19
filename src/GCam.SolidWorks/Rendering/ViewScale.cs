using System;
using GCam.Core;
using GCam.Core.Geometry.Primitives;
using GCam.SolidWorks.Rendering.Interop;

namespace GCam.SolidWorks.Rendering
{
    /// <summary>
    /// What a screen pixel is worth in model millimetres, for whatever view is being
    /// drawn right now.
    /// </summary>
    /// <remarks>
    /// <b>Read from OpenGL, not from the SOLIDWORKS API.</b> IModelView::Scale2 and the
    /// window size would answer the same question, but only approximately and only for a
    /// view we would have to go and find - whereas inside BufferSwapNotify the exact
    /// matrices SOLIDWORKS is about to draw with are already current and cost two reads.
    /// It also means this works whichever window fired the notification, which the API
    /// route does not.
    ///
    /// Measured by projecting two points a known distance apart rather than by taking the
    /// scale out of the matrices. That way it does not matter whether the projection is
    /// orthographic or perspective, or where SOLIDWORKS put the zoom - and under
    /// perspective the answer legitimately differs with depth, which is why the
    /// measurement takes the point it is about.
    ///
    /// Construct once per frame: the matrices do not change while a frame is being drawn,
    /// and reading them per arrow would be the most expensive thing in the draw.
    /// </remarks>
    internal sealed class ViewScale
    {
        /// <summary>The distance projected to measure the scale, millimetres.</summary>
        private const double ProbeMillimetres = 1.0;

        /// <summary>
        /// Below this the two projected points are the same pixel and the division is
        /// noise - a view zoomed so far out that a millimetre is invisible.
        /// </summary>
        private const double MinimumPixels = 1e-6;

        private readonly double[] _modelView = new double[16];
        private readonly double[] _projection = new double[16];
        private readonly int[] _viewport = new int[4];

        private ViewScale()
        {
        }

        /// <summary>
        /// Reads the current OpenGL view. Only valid inside BufferSwapNotify.
        /// </summary>
        public static ViewScale Current()
        {
            var scale = new ViewScale();

            Gl.GetDoublev(Gl.GL_MODELVIEW_MATRIX, scale._modelView);
            Gl.GetDoublev(Gl.GL_PROJECTION_MATRIX, scale._projection);
            Gl.GetIntegerv(Gl.GL_VIEWPORT, scale._viewport);

            return scale;
        }

        /// <summary>
        /// Millimetres per pixel at a point, or zero when it cannot be measured - which
        /// is the answer for anything behind the camera or degenerate.
        /// </summary>
        /// <param name="pointMillimetres">Part coordinates, millimetres.</param>
        public double MillimetresPerPixel(Vec3 pointMillimetres)
        {
            Vec3 point = ToMetres(pointMillimetres);

            // The world direction that maps to the screen's X axis: the first row of the
            // modelview's rotation, which in column-major storage is elements 0, 4 and 8.
            // Normalised, because SOLIDWORKS may carry the zoom as a scale in here.
            var right = new Vec3(_modelView[0], _modelView[4], _modelView[8]);

            if (right.Length <= Precision.Epsilon)
            {
                return 0;
            }

            Vec3 probe = point + (right.Normalised() * Units.MillimetresToMetres(ProbeMillimetres));

            double fromX, fromY, toX, toY;

            if (!Project(point, out fromX, out fromY) || !Project(probe, out toX, out toY))
            {
                return 0;
            }

            double pixels = Math.Sqrt(
                ((toX - fromX) * (toX - fromX)) + ((toY - fromY) * (toY - fromY)));

            return pixels <= MinimumPixels ? 0 : ProbeMillimetres / pixels;
        }

        /// <summary>
        /// A point in metres to a pixel in the window. False when it is behind the eye.
        /// </summary>
        private bool Project(Vec3 point, out double windowX, out double windowY)
        {
            windowX = 0;
            windowY = 0;

            double eyeX = Apply(_modelView, 0, point, 1);
            double eyeY = Apply(_modelView, 1, point, 1);
            double eyeZ = Apply(_modelView, 2, point, 1);
            double eyeW = Apply(_modelView, 3, point, 1);

            var eye = new Vec3(eyeX, eyeY, eyeZ);

            double clipX = Apply(_projection, 0, eye, eyeW);
            double clipY = Apply(_projection, 1, eye, eyeW);
            double clipW = Apply(_projection, 3, eye, eyeW);

            // Zero divides, and negative is behind the eye under a perspective
            // projection - where the projected point is mathematically fine and visually
            // nonsense.
            if (clipW <= Precision.Epsilon)
            {
                return false;
            }

            windowX = _viewport[0] + (_viewport[2] * ((clipX / clipW) + 1) / 2);
            windowY = _viewport[1] + (_viewport[3] * ((clipY / clipW) + 1) / 2);

            return true;
        }

        /// <summary>
        /// One row of a column-major 4x4 times a point - so element (row, column) is at
        /// <c>m[column * 4 + row]</c>.
        /// </summary>
        private static double Apply(double[] m, int row, Vec3 point, double w) =>
            (m[row] * point.X) +
            (m[4 + row] * point.Y) +
            (m[8 + row] * point.Z) +
            (m[12 + row] * w);

        private static Vec3 ToMetres(Vec3 millimetres) => new Vec3(
            Units.MillimetresToMetres(millimetres.X),
            Units.MillimetresToMetres(millimetres.Y),
            Units.MillimetresToMetres(millimetres.Z));
    }
}
