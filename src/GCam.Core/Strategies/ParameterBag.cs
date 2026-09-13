using System;
using System.Collections.Generic;
using System.Globalization;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// A strategy's parameters as named values, independent of any file format.
    /// </summary>
    /// <remarks>
    /// Settings are typed classes; this is how they cross into and out of a file. The
    /// indirection earns its place because **the same names have to serve two formats**:
    /// the part document, and the template files that carry an operation's settings
    /// between parts. Writing settings straight to XML would mean writing them twice.
    ///
    /// The names are the compatibility contract - renaming one silently drops whatever was
    /// stored under the old name - so they are spelled out in each settings class rather
    /// than derived from property names by reflection.
    ///
    /// **Geometry is deliberately not here.** Selections are references into one specific
    /// part, and a template that carried them would be meaningless anywhere else. That is
    /// also why HSMWorks' own templates contain no geometry.
    ///
    /// Everything is stored as an invariant-culture string, so a file written on a machine
    /// with a decimal comma reads correctly on one without.
    /// </remarks>
    public sealed class ParameterBag
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ParameterBag()
        {
        }

        public ParameterBag(IEnumerable<KeyValuePair<string, string>> values)
        {
            foreach (KeyValuePair<string, string> pair in values ?? new KeyValuePair<string, string>[0])
            {
                _values[pair.Key] = pair.Value;
            }
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public int Count => _values.Count;

        public bool Has(string name) => name != null && _values.ContainsKey(name);

        public void Set(string name, double value) =>
            Store(name, value.ToString("R", CultureInfo.InvariantCulture));

        public void Set(string name, int value) =>
            Store(name, value.ToString(CultureInfo.InvariantCulture));

        public void Set(string name, bool value) => Store(name, value ? "true" : "false");

        public void Set(string name, string value) => Store(name, value);

        public void SetEnum<TEnum>(string name, TEnum value) where TEnum : struct =>
            Store(name, value.ToString());

        /// <summary>
        /// The stored value, or <paramref name="fallback"/> when it is absent or
        /// unreadable.
        /// </summary>
        /// <remarks>
        /// Unreadable falls back rather than throwing, all the way through. A parameter
        /// this build does not understand is the normal cost of a file written by another
        /// one, and losing a whole operation over one bad number would be a poor trade.
        /// </remarks>
        public double GetDouble(string name, double fallback = 0)
        {
            return TryRead(name, out string raw)
                   && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : fallback;
        }

        public int GetInt(string name, int fallback = 0)
        {
            return TryRead(name, out string raw)
                   && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        public bool GetBool(string name, bool fallback = false)
        {
            return TryRead(name, out string raw) && bool.TryParse(raw, out bool value)
                ? value
                : fallback;
        }

        public string GetString(string name, string fallback = null) =>
            TryRead(name, out string raw) ? raw : fallback;

        public TEnum GetEnum<TEnum>(string name, TEnum fallback) where TEnum : struct
        {
            return TryRead(name, out string raw) && Enum.TryParse(raw, true, out TEnum value)
                ? value
                : fallback;
        }

        private void Store(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A parameter needs a name.", nameof(name));
            }

            if (value == null)
            {
                _values.Remove(name);
                return;
            }

            _values[name] = value;
        }

        private bool TryRead(string name, out string value)
        {
            value = null;
            return name != null && _values.TryGetValue(name, out value);
        }

        public override string ToString() => $"{_values.Count} parameters";
    }
}
