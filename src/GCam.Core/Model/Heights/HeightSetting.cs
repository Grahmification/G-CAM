namespace GCam.Core.Model.Heights
{
    /// <summary>
    /// One machining height: what it is measured from, plus a signed offset.
    /// </summary>
    /// <remarks>
    /// This shape - mode plus offset rather than a plain number - is what makes heights
    /// follow the stock. Change the stock and every height derived from it moves, with
    /// nothing to re-enter. It is the part of HSMWorks' height model worth copying.
    ///
    /// Millimetres, in the operation's frame.
    /// </remarks>
    public sealed class HeightSetting
    {
        public HeightMode Mode { get; set; } = HeightMode.FromStockTop;

        /// <summary>
        /// Distance above the datum. Signed: negative goes below it, which is how a
        /// through cut is expressed as "model bottom, -0.5mm".
        /// </summary>
        public double Offset { get; set; }

        /// <summary>
        /// The picked entity, for <see cref="HeightMode.FromSelection"/> only. Null for
        /// every other mode, and kept rather than cleared when the mode changes away, so
        /// switching mode and back does not lose the pick.
        /// </summary>
        public GeometryRef Reference { get; set; }

        public HeightSetting()
        {
        }

        public HeightSetting(HeightMode mode, double offset = 0)
        {
            Mode = mode;
            Offset = offset;
        }

        /// <summary>
        /// The Z this height lands on. False when the mode needs a selection and that
        /// selection is missing or no longer in the model.
        /// </summary>
        /// <remarks>
        /// Failure is not an exception: a reference that stopped resolving is a warning
        /// on the operation naming the entity, not a crash mid-generate.
        /// </remarks>
        public bool TryResolve(HeightContext context, out double z)
        {
            z = 0;

            if (context == null)
            {
                return false;
            }

            if (Mode == HeightMode.FromSelection)
            {
                if (Reference == null || Reference.IsEmpty)
                {
                    return false;
                }

                if (!context.TryGetSelection(Reference.PersistentId, out double picked))
                {
                    return false;
                }

                z = picked + Offset;
                return true;
            }

            z = context.Datum(Mode) + Offset;
            return true;
        }

        /// <summary>Why <see cref="TryResolve"/> failed, or null when it would succeed.</summary>
        public string DescribeFailure(HeightContext context)
        {
            if (TryResolve(context, out double _))
            {
                return null;
            }

            if (Mode != HeightMode.FromSelection)
            {
                return "no stock or model extent is available";
            }

            if (Reference == null || Reference.IsEmpty)
            {
                return "it is measured from a selection, but nothing is selected";
            }

            return $"the selected {Reference} is no longer in the model";
        }

        public HeightSetting Clone()
        {
            return new HeightSetting
            {
                Mode = Mode,
                Offset = Offset,
                Reference = Reference?.Clone(),
            };
        }
    }
}
