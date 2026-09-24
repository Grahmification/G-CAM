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
            double clearance,
            double retract,
            double feed,
            double top,
            double bottom,
            double? retractLiftedFrom = null)
        {
            Clearance = clearance;
            Retract = retract;
            Feed = feed;
            Top = top;
            Bottom = bottom;
            RetractLiftedFrom = retractLiftedFrom;
        }

        /// <summary>
        /// The retract height as entered, when it was below the feed height and
        /// <see cref="Retract"/> has been lifted to it; null when it was used as entered.
        /// </summary>
        /// <remarks>
        /// Kept so the lift can be reported: the path is still generated, but it does not
        /// retract where the page says, and whoever runs it should be told.
        /// </remarks>
        public double? RetractLiftedFrom { get; }

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
