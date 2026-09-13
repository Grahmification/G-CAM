using System.Collections.Generic;
using GCam.Core.Model;

namespace GCam.Core.Generation
{
    /// <summary>
    /// How far a generation run has got.
    /// </summary>
    /// <remarks>
    /// Reported often enough to drive a percentage on the operation and a progress bar on
    /// the job. <see cref="Fraction"/> spans the whole run, not the current operation, so a
    /// job of ten operations does not show ten bars filling in turn.
    /// </remarks>
    public sealed class GenerationProgress
    {
        public GenerationProgress(
            Operation operation, int completed, int total, double operationFraction)
        {
            Operation = operation;
            Completed = completed;
            Total = total;
            OperationFraction = Clamp(operationFraction);
        }

        /// <summary>What is being worked on. Null once the run has finished.</summary>
        public Operation Operation { get; }

        /// <summary>How many operations are finished with.</summary>
        public int Completed { get; }

        public int Total { get; }

        /// <summary>How far through the current operation, 0 to 1.</summary>
        public double OperationFraction { get; }

        /// <summary>How far through the whole run, 0 to 1.</summary>
        public double Fraction =>
            Total <= 0 ? 1.0 : Clamp((Completed + OperationFraction) / Total);

        /// <summary>The percentage an operation shows while it is being generated.</summary>
        public int OperationPercent => (int)(OperationFraction * 100);

        private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public override string ToString() =>
            $"{Completed}/{Total} ({Fraction:P0}) {Operation?.Name}";
    }

    /// <summary>
    /// What a generation run did.
    /// </summary>
    /// <remarks>
    /// The per-operation outcome lives on each operation's <see cref="Operation.State"/> -
    /// this is the summary for the log and for whatever kicked the run off.
    /// </remarks>
    public sealed class GenerationResult
    {
        public GenerationResult(
            int generated, int failed, int skipped, bool cancelled, IReadOnlyList<Operation> failures)
        {
            Generated = generated;
            Failed = failed;
            Skipped = skipped;
            Cancelled = cancelled;
            Failures = failures ?? new Operation[0];
        }

        public int Generated { get; }

        public int Failed { get; }

        /// <summary>Disabled operations, which are not generated.</summary>
        public int Skipped { get; }

        public bool Cancelled { get; }

        public IReadOnlyList<Operation> Failures { get; }

        public bool AllSucceeded => Failed == 0 && !Cancelled;

        public override string ToString() =>
            $"{Generated} generated, {Failed} failed, {Skipped} skipped" +
            (Cancelled ? ", cancelled" : string.Empty);
    }
}
