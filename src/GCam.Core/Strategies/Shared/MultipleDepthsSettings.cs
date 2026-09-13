using System.Collections.Generic;

namespace GCam.Core.Strategies.Shared
{
    /// <summary>
    /// Taking the full depth in several axial passes.
    /// </summary>
    /// <remarks>
    /// Shared by the strategies that cut down into material and absent from the ones that
    /// do not - drilling has no equivalent, which is why this is composed into the
    /// strategies that want it rather than inherited by all of them.
    ///
    /// HSMWorks calls these <c>doMultipleDepths</c>, <c>maximumStepdown</c> and
    /// <c>useEvenStepdowns</c>.
    /// </remarks>
    public sealed class MultipleDepthsSettings
    {
        /// <summary>Off means one pass at the bottom height.</summary>
        public bool Enabled { get; set; }

        /// <summary>Deepest axial cut in one pass, mm.</summary>
        public double MaximumStepdown { get; set; } = 1.0;

        /// <summary>
        /// Spread the depth evenly rather than taking full steps and a thin remainder.
        /// </summary>
        /// <remarks>
        /// On by default: a last pass of 0.05mm loads the cutter differently from every
        /// pass before it, and that shows up in the finish.
        /// </remarks>
        public bool UseEvenStepdowns { get; set; } = true;

        /// <summary>
        /// How many passes a given depth takes, and how deep each one is.
        /// </summary>
        /// <remarks>
        /// Zero passes when disabled or when there is no depth - the caller decides what
        /// a single full-depth pass means, because that differs per strategy.
        /// </remarks>
        public int PassCount(double depthOfCut)
        {
            if (!Enabled || depthOfCut <= Precision.Epsilon || MaximumStepdown <= Precision.Epsilon)
            {
                return 0;
            }

            // Ceiling: the remainder is a pass too, however thin.
            return (int)System.Math.Ceiling(depthOfCut / MaximumStepdown - Precision.Epsilon);
        }

        /// <summary>
        /// The actual depth of each pass for a given total, mm. Even when
        /// <see cref="UseEvenStepdowns"/>, otherwise full steps and a remainder.
        /// </summary>
        public IReadOnlyList<double> Stepdowns(double depthOfCut)
        {
            var steps = new List<double>();

            int passes = PassCount(depthOfCut);
            if (passes == 0)
            {
                return steps;
            }

            if (UseEvenStepdowns)
            {
                double even = depthOfCut / passes;
                for (int i = 0; i < passes; i++)
                {
                    steps.Add(even);
                }

                return steps;
            }

            double left = depthOfCut;
            while (left > Precision.Epsilon)
            {
                double step = System.Math.Min(MaximumStepdown, left);
                steps.Add(step);
                left -= step;
            }

            return steps;
        }

        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            if (Enabled && MaximumStepdown <= Precision.Epsilon)
            {
                problems.Add("Maximum stepdown must be greater than zero when multiple depths are used.");
            }

            return problems;
        }

        public MultipleDepthsSettings Clone() => (MultipleDepthsSettings)MemberwiseClone();
    }
}
