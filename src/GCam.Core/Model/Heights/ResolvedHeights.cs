namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// The five heights of an operation as plain Z values, in millimetres in the
    /// operation's frame.
    /// </summary>
    /// <remarks>
    /// What a strategy actually machines against. Immutable, because resolution happens
    /// once per generate and everything downstream reads the same numbers.
    /// </remarks>
    public sealed class ResolvedHeights
    {
        public ResolvedHeights(
            double clearance, double retract, double feed, double top, double bottom)
        {
            Clearance = clearance;
            Retract = retract;
            Feed = feed;
            Top = top;
            Bottom = bottom;
        }

        /// <summary>The plane rapids cross at, above everything including clamps.</summary>
        public double Clearance { get; }

        /// <summary>Where the tool goes between passes.</summary>
        public double Retract { get; }

        /// <summary>Where rapid becomes feed on the way down.</summary>
        public double Feed { get; }

        /// <summary>Where cutting starts.</summary>
        public double Top { get; }

        /// <summary>Where cutting stops.</summary>
        public double Bottom { get; }

        /// <summary>Total depth to be removed. Positive for any valid pair.</summary>
        public double DepthOfCut => Top - Bottom;
    }
}
