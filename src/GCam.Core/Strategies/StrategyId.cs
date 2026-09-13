using System;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// The stable name of a machining strategy, as it appears in saved documents and
    /// template files.
    /// </summary>
    /// <remarks>
    /// A value type over a string rather than an enum, because this identity has to
    /// survive a round trip through a file written by another build - and rather than a
    /// bare string, because it appears in catalogue lookups, persistence and template
    /// import, where passing the wrong string would compile perfectly.
    ///
    /// The names match HSMWorks' own <c>strategy</c> attribute - "contour2d", "drill",
    /// "face", "adaptive2d" - so importing one of their templates is a direct lookup
    /// rather than a translation table. Comparison is case-insensitive for the same
    /// reason: nothing guarantees another tool's casing.
    /// </remarks>
    public readonly struct StrategyId : IEquatable<StrategyId>
    {
        private readonly string _value;

        public StrategyId(string value)
        {
            _value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>2D contour: follow selected edges at a depth. The first one built.</summary>
        public static readonly StrategyId Contour2d = new StrategyId("contour2d");

        /// <summary>Facing: clear a flat area down to a height.</summary>
        public static readonly StrategyId Face = new StrategyId("face");

        /// <summary>2D adaptive clearing: constant tool engagement roughing.</summary>
        public static readonly StrategyId Adaptive2d = new StrategyId("adaptive2d");

        /// <summary>Drilling and the canned cycles around it.</summary>
        public static readonly StrategyId Drill = new StrategyId("drill");

        /// <summary>Never null. An unset id reads as an empty string.</summary>
        public string Value => _value ?? string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(_value);

        public bool Equals(StrategyId other) =>
            StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

        public override bool Equals(object obj) => obj is StrategyId other && Equals(other);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

        public static bool operator ==(StrategyId left, StrategyId right) => left.Equals(right);

        public static bool operator !=(StrategyId left, StrategyId right) => !left.Equals(right);

        public override string ToString() => Value;
    }
}
