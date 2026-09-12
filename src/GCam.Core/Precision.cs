namespace GCam.Core
{
    /// <summary>
    /// Thresholds for comparing floating-point numbers.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT called "tolerance". In CAM that word means a machining
    /// tolerance - how far the toolpath may deviate from the model - and confusing the
    /// two would be expensive. Values here are about the limits of double arithmetic
    /// and nothing else.
    ///
    /// Machining tolerances belong to the operation that needs them and are passed
    /// explicitly, like the chord tolerance on <see cref="Tooling.CutterProfile"/>.
    /// </remarks>
    public static class Precision
    {
        /// <summary>
        /// Two lengths closer than this are the same length.
        /// </summary>
        /// <remarks>
        /// Well below any real dimension in millimetres - a micron is 1e-3 - so this
        /// only ever absorbs accumulated floating-point error, never a real difference.
        /// </remarks>
        public const double Epsilon = 1e-9;
    }
}
