using GCam.Core.Geometry.Primitives;

namespace GCam.SolidWorks.Extraction
{
    /// <summary>
    /// The two transforms between a job's coordinate system and the part's, both in
    /// millimetres.
    /// </summary>
    /// <remarks>
    /// They come as a pair because almost every use needs both: geometry is measured in
    /// part space and has to be understood in job space, and the result is then drawn
    /// back in part space. Keeping them together is also what removes the need for a
    /// matrix inverse in Core - SOLIDWORKS supplies both directions, so nothing has to
    /// compute one.
    /// </remarks>
    internal struct JobFrame
    {
        public JobFrame(Matrix4 toPart, Matrix4 toJob)
        {
            ToPart = toPart;
            ToJob = toJob;
        }

        /// <summary>Job coordinates into part coordinates.</summary>
        public Matrix4 ToPart { get; }

        /// <summary>Part coordinates into job coordinates.</summary>
        public Matrix4 ToJob { get; }

        /// <summary>The frame of a job using the part origin, where the two are the same.</summary>
        public static JobFrame PartOrigin => new JobFrame(Matrix4.Identity, Matrix4.Identity);
    }
}
