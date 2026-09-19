using System.Collections.Generic;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Model
{
    /// <summary>
    /// How the raw material is sized relative to the model.
    /// </summary>
    /// <remarks>
    /// More modes are expected - a cylinder for turning stock, or a second solid body
    /// standing in for a casting. Each new mode adds parameters rather than changing the
    /// existing ones, so a job written under one mode still reads correctly.
    /// </remarks>
    public enum StockMode
    {
        /// <summary>Offsets from the model, with one value shared by all four sides.</summary>
        RelativeBox = 0,

        /// <summary>Offsets from the model, with X and Y given separately.</summary>
        RelativeBoxXY = 1,

        /// <summary>Absolute dimensions, independent of how big the model is.</summary>
        FixedSizeBox = 2,
    }

    /// <summary>
    /// The stock for a job: a box around the model, described one of several ways.
    /// </summary>
    /// <remarks>
    /// Every length is in millimetres, like everything else in Core.
    ///
    /// Only the fields belonging to the current <see cref="Mode"/> are read. The others
    /// keep their values so that switching mode and switching back does not lose what
    /// was typed.
    /// </remarks>
    public sealed class Stock
    {
        public StockMode Mode { get; set; } = StockMode.RelativeBox;

        /// <summary>Material above the top of the model. All relative modes.</summary>
        public double TopOffset { get; set; }

        /// <summary>Material below the bottom of the model. All relative modes.</summary>
        public double BottomOffset { get; set; }

        /// <summary>Material on all four sides. <see cref="StockMode.RelativeBox"/> only.</summary>
        public double SideOffset { get; set; }

        /// <summary>Material on the left and right. <see cref="StockMode.RelativeBoxXY"/> only.</summary>
        public double OffsetX { get; set; }

        /// <summary>Material front and back. <see cref="StockMode.RelativeBoxXY"/> only.</summary>
        public double OffsetY { get; set; }

        /// <summary>Absolute X size. <see cref="StockMode.FixedSizeBox"/> only.</summary>
        public double Width { get; set; }

        /// <summary>Absolute Y size. <see cref="StockMode.FixedSizeBox"/> only.</summary>
        public double Depth { get; set; }

        /// <summary>Absolute Z size. <see cref="StockMode.FixedSizeBox"/> only.</summary>
        public double Height { get; set; }

        /// <summary>
        /// The stock box around a given model extent.
        /// </summary>
        /// <remarks>
        /// For the fixed-size mode the model is centred in X and Y, and the **top** of
        /// the stock is put at the top of the model. Top alignment rather than centring
        /// in Z because the top face is what gets touched off, so keeping it where the
        /// model's top is means Z0 does not move when the stock size changes.
        ///
        /// Nothing here refuses stock smaller than the model. That is a real mistake but
        /// <see cref="Validate"/> cannot see the model, so the check belongs to whoever
        /// has both - see <see cref="FitsAround"/>.
        /// </remarks>
        public Bounds ComputeBounds(Bounds model)
        {
            switch (Mode)
            {
                case StockMode.RelativeBoxXY:
                    return model.Expanded(
                        OffsetX, OffsetX,
                        OffsetY, OffsetY,
                        BottomOffset, TopOffset);

                case StockMode.FixedSizeBox:
                    return FixedBoxAround(model);

                case StockMode.RelativeBox:
                default:
                    return model.Expanded(
                        SideOffset, SideOffset,
                        SideOffset, SideOffset,
                        BottomOffset, TopOffset);
            }
        }

        private Bounds FixedBoxAround(Bounds model)
        {
            Vec3 centre = model.Centre;

            double halfWidth = Width / 2.0;
            double halfDepth = Depth / 2.0;

            return new Bounds(
                new Vec3(centre.X - halfWidth, centre.Y - halfDepth, model.Max.Z - Height),
                new Vec3(centre.X + halfWidth, centre.Y + halfDepth, model.Max.Z));
        }

        /// <summary>
        /// True when the computed stock actually contains the model.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Validate"/> because it needs the model extent, which
        /// only the caller holding the geometry has.
        /// </remarks>
        public bool FitsAround(Bounds model)
        {
            Bounds stock = ComputeBounds(model);

            return stock.Min.X <= model.Min.X + Precision.Epsilon
                   && stock.Min.Y <= model.Min.Y + Precision.Epsilon
                   && stock.Min.Z <= model.Min.Z + Precision.Epsilon
                   && stock.Max.X >= model.Max.X - Precision.Epsilon
                   && stock.Max.Y >= model.Max.Y - Precision.Epsilon
                   && stock.Max.Z >= model.Max.Z - Precision.Epsilon;
        }

        /// <summary>
        /// Problems a user can act on. Empty when the stock is usable.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            // The relative modes have nothing to check. An offset may be any sign: zero
            // machines exactly to the model, and negative puts the stock face inside it,
            // which is what a part already roughed elsewhere looks like. Whether the
            // offsets leave a box with any size in it cannot be answered here, because
            // that depends on the model they are measured from - see ComputeBounds.
            switch (Mode)
            {
                case StockMode.FixedSizeBox:
                    RequirePositive(problems, Width, "Stock width");
                    RequirePositive(problems, Depth, "Stock depth");
                    RequirePositive(problems, Height, "Stock height");
                    break;
            }

            return problems;
        }

        private static void RequirePositive(ICollection<string> problems, double value, string label)
        {
            if (value <= Precision.Epsilon)
            {
                problems.Add($"{label} must be greater than zero.");
            }
        }

        public Stock Clone() => (Stock)MemberwiseClone();
    }
}
