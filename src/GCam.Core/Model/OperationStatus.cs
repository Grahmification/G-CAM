namespace GCam.Core.Model
{
    /// <summary>
    /// How an operation's state is presented: which badge it earns, and what it says.
    /// </summary>
    /// <remarks>
    /// In Core rather than the viewmodel for the reason architecture.md gives for
    /// <c>ToolSearch</c>: this is a rule, not presentation state, and Core is the only
    /// place a headless test can reach it. The tree owns *where* the badge is drawn; this
    /// owns *whether* there is one.
    ///
    /// Keeping the wording here too means the tooltip, and later the posting warning and
    /// the generation report, say the same thing about the same state. Two vocabularies
    /// for six states is how "Stale" ends up called three different things in one product.
    /// </remarks>
    public static class OperationStatus
    {
        /// <summary>
        /// The mark the tree puts on an operation with this state.
        /// </summary>
        /// <remarks>
        /// **Generating is an error badge, not a blank one.** An operation being computed
        /// has no toolpath worth believing yet, and the badge it already carried is what
        /// it should keep until the run says otherwise - blanking it mid-run would read as
        /// "done" for as long as the run takes.
        /// </remarks>
        public static OperationBadge Badge(OperationState state)
        {
            switch (state)
            {
                case OperationState.Generated:
                    return OperationBadge.None;

                case OperationState.Warning:
                    return OperationBadge.Warning;

                default:
                    // NotGenerated, Generating, Stale, Failed - nothing to trust.
                    return OperationBadge.Error;
            }
        }

        /// <summary>A few words for the state on its own.</summary>
        public static string Label(OperationState state)
        {
            switch (state)
            {
                case OperationState.NotGenerated:
                    return "Not generated";

                case OperationState.Generating:
                    return "Generating…";

                case OperationState.Generated:
                    return "Generated";

                case OperationState.Stale:
                    return "Out of date — generate it again";

                case OperationState.Warning:
                    return "Generated, with a warning";

                case OperationState.Failed:
                    return "Generation failed";

                default:
                    return state.ToString();
            }
        }

        /// <summary>
        /// What the tree's tooltip says about an operation, or null when there is nothing
        /// to say.
        /// </summary>
        /// <remarks>
        /// The state's own words, then <see cref="Operation.StateMessage"/> underneath
        /// when there is one - which for a failure is the only place the reason appears
        /// outside the log.
        ///
        /// Null rather than an empty string for an operation that is not there: WPF
        /// suppresses a null tooltip and shows an empty box for "".
        /// </remarks>
        public static string Describe(Operation operation)
        {
            if (operation == null)
            {
                return null;
            }

            string label = Label(operation.State);

            if (!operation.Enabled)
            {
                // Worth saying outright. A suppressed operation is skipped by generation,
                // so its state is whatever it was before it was suppressed and will not
                // change however many times the job is generated.
                label = "Suppressed — " + label;
            }

            return string.IsNullOrWhiteSpace(operation.StateMessage)
                ? label
                : label + "\n" + operation.StateMessage.Trim();
        }
    }
}
