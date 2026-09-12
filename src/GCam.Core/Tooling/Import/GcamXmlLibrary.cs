using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using GCam.Core.Diagnostics;

namespace GCam.Core.Tooling.Import
{
    /// <summary>
    /// Shared constants for G-CAM's native tool library format.
    /// </summary>
    /// <remarks>
    /// The file looks like this:
    ///
    /// <code>
    /// &lt;gcamToolLibrary version="1" units="mm" id="..." name="Shop tools"&gt;
    ///   &lt;holders&gt;
    ///     &lt;holder id="h1" name="BT40 ER32"&gt;
    ///       &lt;segment length="30" lowerDiameter="20" upperDiameter="30" /&gt;
    ///     &lt;/holder&gt;
    ///   &lt;/holders&gt;
    ///   &lt;tools&gt;
    ///     &lt;tool id="t1" number="3" name="10mm bull nose" type="BullNoseEndMill" holderId="h1"&gt;
    ///       &lt;geometry diameter="10" cornerRadius="2" fluteLength="25" fluteCount="4" /&gt;
    ///       &lt;cutting spindleRpm="8000" cuttingFeed="1200" plungeFeed="300"
    ///                stepover="4" stepdown="2" coolant="Flood" /&gt;
    ///     &lt;/tool&gt;
    ///   &lt;/tools&gt;
    /// &lt;/gcamToolLibrary&gt;
    /// </code>
    ///
    /// Holders are separate entries that tools reference, because a shop has a handful
    /// of holders and hundreds of tools.
    ///
    /// Lengths are stored in the unit named by the root, and converted to millimetres
    /// on load - Core works exclusively in mm. Angles are always degrees.
    /// </remarks>
    public static class GcamXmlLibrary
    {
        public const string RootElement = "gcamToolLibrary";

        /// <summary>
        /// Format version. Bump when making a change a previous reader could not
        /// understand; readers refuse anything newer than they know.
        /// </summary>
        public const int CurrentVersion = 1;

        public const string FileExtension = ".gcamtools";
    }

    /// <summary>Reads G-CAM's native tool library XML.</summary>
    public sealed class GcamXmlLibraryReader : IToolLibraryReader
    {
        public string FormatName => "G-CAM tool library";

        public string FileExtension => GcamXmlLibrary.FileExtension;

        public ToolLibraryReadResult Read(Stream stream, string sourcePath)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            XDocument document;
            try
            {
                document = XDocument.Load(stream);
            }
            catch (XmlException ex)
            {
                throw new GCamUserException(
                    $"'{Describe(sourcePath)}' is not valid XML: {ex.Message}", ex);
            }

            XElement root = document.Root;
            if (root == null || root.Name.LocalName != GcamXmlLibrary.RootElement)
            {
                throw new GCamUserException(
                    $"'{Describe(sourcePath)}' is not a G-CAM tool library " +
                    $"(expected a <{GcamXmlLibrary.RootElement}> root element).");
            }

            int version = ReadInt(root, "version", 1);
            if (version > GcamXmlLibrary.CurrentVersion)
            {
                throw new GCamUserException(
                    $"'{Describe(sourcePath)}' was written by a newer version of G-CAM " +
                    $"(file format {version}, this build understands {GcamXmlLibrary.CurrentVersion}).");
            }

            double toMillimetres = ReadUnitScale(root, sourcePath);

            var library = new ToolLibrary
            {
                Id = ReadString(root, "id") ?? Guid.NewGuid().ToString("D"),
                Name = ReadString(root, "name"),
                SourcePath = sourcePath,
            };

            foreach (XElement element in root.Elements("holders").Elements("holder"))
            {
                library.Holders.Add(ReadHolder(element, toMillimetres));
            }

            foreach (XElement element in root.Elements("tools").Elements("tool"))
            {
                library.Tools.Add(ReadTool(element, library, toMillimetres, sourcePath));
            }

            IReadOnlyList<string> problems = library.Validate();
            if (problems.Count > 0)
            {
                throw new GCamUserException(
                    $"'{Describe(sourcePath)}' contains invalid tools:" + Environment.NewLine +
                    string.Join(Environment.NewLine, problems.Take(10)));
            }

            return new ToolLibraryReadResult(library, new List<string>());
        }

        private static double ReadUnitScale(XElement root, string sourcePath)
        {
            string units = (ReadString(root, "units") ?? "mm").Trim().ToLowerInvariant();
            switch (units)
            {
                case "mm":
                case "millimetre":
                case "millimeter":
                    return 1.0;

                case "in":
                case "inch":
                case "inches":
                    return Units.MillimetresPerInch;

                default:
                    throw new GCamUserException(
                        $"'{Describe(sourcePath)}' declares unknown units '{units}'. Use \"mm\" or \"inch\".");
            }
        }

        private static Holder ReadHolder(XElement element, double scale)
        {
            var holder = new Holder
            {
                Id = ReadString(element, "id"),
                Name = ReadString(element, "name"),
                Comment = ReadString(element, "comment"),
                Vendor = ReadString(element, "vendor"),
                ProductId = ReadString(element, "productId"),
            };

            foreach (XElement segment in element.Elements("segment"))
            {
                holder.Segments.Add(new HolderSegment(
                    ReadDouble(segment, "length") * scale,
                    ReadDouble(segment, "lowerDiameter") * scale,
                    ReadDouble(segment, "upperDiameter") * scale));
            }

            return holder;
        }

        private static Tool ReadTool(XElement element, ToolLibrary library, double scale, string sourcePath)
        {
            var tool = new Tool
            {
                Id = ReadString(element, "id") ?? Guid.NewGuid().ToString("D"),
                Number = ReadInt(element, "number", 0),
                Name = ReadString(element, "name"),
                Type = ReadToolType(element, sourcePath),
            };

            XElement geometry = element.Element("geometry");
            if (geometry == null)
            {
                throw new GCamUserException(
                    $"Tool '{tool.Name ?? tool.Id}' in '{Describe(sourcePath)}' has no <geometry> element.");
            }

            tool.Geometry = new ToolGeometry
            {
                Diameter = ReadDouble(geometry, "diameter") * scale,
                CornerRadius = ReadDouble(geometry, "cornerRadius") * scale,
                TipDiameter = ReadDouble(geometry, "tipDiameter") * scale,
                FluteLength = ReadDouble(geometry, "fluteLength") * scale,
                ShoulderLength = ReadDouble(geometry, "shoulderLength") * scale,
                BodyLength = ReadDouble(geometry, "bodyLength") * scale,
                ThreadPitch = ReadDouble(geometry, "threadPitch") * scale,
                ShankDiameter = ReadDouble(geometry, "shankDiameter") * scale,
                OverallLength = ReadDouble(geometry, "overallLength") * scale,

                // Angles are degrees regardless of the file's length units.
                TipAngle = ReadDouble(geometry, "tipAngle"),
                SecondTipAngle = ReadDouble(geometry, "secondTipAngle"),
                ThreadProfileAngle = ReadDouble(geometry, "threadProfileAngle"),
                FluteCount = ReadInt(geometry, "fluteCount", 0),
            };

            tool.Comment = ReadString(element, "comment");
            tool.Manufacturer = ReadString(element, "manufacturer");
            tool.ProductId = ReadString(element, "productId");
            tool.Material = ReadString(element, "material");

            XElement machine = element.Element("machine");
            if (machine != null)
            {
                tool.Machine = new MachineData
                {
                    DiameterOffset = ReadInt(machine, "diameterOffset", 0),
                    LengthOffset = ReadInt(machine, "lengthOffset", 0),
                    Turret = ReadInt(machine, "turret", 0),
                    BreakControl = ReadBool(machine, "breakControl"),
                    ManualToolChange = ReadBool(machine, "manualToolChange"),
                };
            }

            foreach (XElement item in element.Elements("extra").Elements("item"))
            {
                string key = ReadString(item, "key");
                if (!string.IsNullOrEmpty(key))
                {
                    tool.Extra[key] = ReadString(item, "value") ?? string.Empty;
                }
            }

            XElement cutting = element.Element("cutting");
            if (cutting != null)
            {
                tool.Cutting = new CuttingData
                {
                    SpindleRpm = ReadDouble(cutting, "spindleRpm"),

                    // Feeds are a length per minute, so they scale with the file's units.
                    CuttingFeed = ReadDouble(cutting, "cuttingFeed") * scale,
                    PlungeFeed = ReadDouble(cutting, "plungeFeed") * scale,
                    Stepover = ReadDouble(cutting, "stepover") * scale,
                    Stepdown = ReadDouble(cutting, "stepdown") * scale,
                    EntryFeed = ReadDouble(cutting, "entryFeed") * scale,
                    ExitFeed = ReadDouble(cutting, "exitFeed") * scale,
                    RampFeed = ReadDouble(cutting, "rampFeed") * scale,
                    RetractFeed = ReadDouble(cutting, "retractFeed") * scale,
                    RampSpindleRpm = ReadDouble(cutting, "rampSpindleRpm"),
                    SpindleClockwise = ReadBool(cutting, "spindleClockwise", fallback: true),
                    FeedMode = ReadFeedMode(cutting),
                    Coolant = ReadCoolant(cutting),
                };
            }

            string holderId = ReadString(element, "holderId");
            if (!string.IsNullOrEmpty(holderId))
            {
                tool.Holder = library.FindHolderById(holderId);
                if (tool.Holder == null)
                {
                    throw new GCamUserException(
                        $"Tool '{tool.Name ?? tool.Id}' references holder '{holderId}', " +
                        $"which is not defined in '{Describe(sourcePath)}'.");
                }
            }

            return tool;
        }

        private static ToolType ReadToolType(XElement element, string sourcePath)
        {
            string raw = ReadString(element, "type");
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new GCamUserException($"A tool in '{Describe(sourcePath)}' has no type.");
            }

            ToolType type;
            if (!Enum.TryParse(raw, ignoreCase: true, result: out type) || !Enum.IsDefined(typeof(ToolType), type))
            {
                throw new GCamUserException(
                    $"Unknown tool type '{raw}' in '{Describe(sourcePath)}'. Known types: " +
                    string.Join(", ", Enum.GetNames(typeof(ToolType))) + ".");
            }

            return type;
        }

        private static FeedMode ReadFeedMode(XElement element)
        {
            string raw = ReadString(element, "feedMode");
            FeedMode mode;
            if (!string.IsNullOrWhiteSpace(raw) &&
                Enum.TryParse(raw, ignoreCase: true, result: out mode) &&
                Enum.IsDefined(typeof(FeedMode), mode))
            {
                return mode;
            }

            return FeedMode.PerMinute;
        }

        private static bool ReadBool(XElement element, string name, bool fallback = false)
        {
            string raw = ReadString(element, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            bool value;
            return bool.TryParse(raw, out value) ? value : fallback;
        }

        private static CoolantMode ReadCoolant(XElement element)
        {
            string raw = ReadString(element, "coolant");
            CoolantMode mode;
            if (!string.IsNullOrWhiteSpace(raw) &&
                Enum.TryParse(raw, ignoreCase: true, result: out mode) &&
                Enum.IsDefined(typeof(CoolantMode), mode))
            {
                return mode;
            }

            // An unrecognised coolant mode is not worth failing a whole library over.
            return CoolantMode.Flood;
        }

        private static string Describe(string sourcePath)
        {
            return string.IsNullOrEmpty(sourcePath) ? "the tool library" : Path.GetFileName(sourcePath);
        }

        private static string ReadString(XElement element, string name)
        {
            return (string)element.Attribute(name);
        }

        private static double ReadDouble(XElement element, string name)
        {
            string raw = ReadString(element, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }

            double value;
            // Invariant culture always: a library written on a machine with comma
            // decimal separators must still load everywhere else.
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new GCamUserException($"'{raw}' is not a valid number for '{name}'.");
            }

            return value;
        }

        private static int ReadInt(XElement element, string name, int fallback)
        {
            string raw = ReadString(element, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            int value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new GCamUserException($"'{raw}' is not a valid whole number for '{name}'.");
            }

            return value;
        }
    }

    /// <summary>Writes G-CAM's native tool library XML. Always millimetres.</summary>
    public sealed class GcamXmlLibraryWriter
    {
        public void Write(IToolLibrary library, Stream stream)
        {
            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            var root = new XElement(
                GcamXmlLibrary.RootElement,
                new XAttribute("version", GcamXmlLibrary.CurrentVersion),
                new XAttribute("units", "mm"),
                new XAttribute("id", library.Id ?? string.Empty),
                new XAttribute("name", library.Name ?? string.Empty));

            if (library.Holders.Count > 0)
            {
                root.Add(new XElement("holders", library.Holders.Select(WriteHolder)));
            }

            root.Add(new XElement("tools", library.Tools.Select(WriteTool)));

            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(stream);
        }

        private static XElement WriteHolder(Holder holder)
        {
            return new XElement(
                "holder",
                new XAttribute("id", holder.Id ?? string.Empty),
                new XAttribute("name", holder.Name ?? string.Empty),
                holder.Comment == null ? null : new XAttribute("comment", holder.Comment),
                holder.Vendor == null ? null : new XAttribute("vendor", holder.Vendor),
                holder.ProductId == null ? null : new XAttribute("productId", holder.ProductId),
                holder.Segments.Select(s => new XElement(
                    "segment",
                    Number("length", s.Length),
                    Number("lowerDiameter", s.LowerDiameter),
                    Number("upperDiameter", s.UpperDiameter))));
        }

        private static XElement WriteTool(Tool tool)
        {
            var element = new XElement(
                "tool",
                new XAttribute("id", tool.Id ?? string.Empty),
                new XAttribute("number", tool.Number),
                new XAttribute("name", tool.Name ?? string.Empty),
                new XAttribute("type", tool.Type.ToString()));

            AddIfPresent(element, "comment", tool.Comment);
            AddIfPresent(element, "manufacturer", tool.Manufacturer);
            AddIfPresent(element, "productId", tool.ProductId);
            AddIfPresent(element, "material", tool.Material);

            if (tool.Holder != null && !string.IsNullOrEmpty(tool.Holder.Id))
            {
                element.Add(new XAttribute("holderId", tool.Holder.Id));
            }

            ToolGeometry g = tool.Geometry ?? new ToolGeometry();
            element.Add(new XElement(
                "geometry",
                Number("diameter", g.Diameter),
                Number("cornerRadius", g.CornerRadius),
                Number("tipAngle", g.TipAngle),
                Number("tipDiameter", g.TipDiameter),
                Number("fluteLength", g.FluteLength),
                Number("shoulderLength", g.ShoulderLength),
                Number("bodyLength", g.BodyLength),
                Number("threadPitch", g.ThreadPitch),
                Number("secondTipAngle", g.SecondTipAngle),
                Number("threadProfileAngle", g.ThreadProfileAngle),
                new XAttribute("fluteCount", g.FluteCount),
                Number("shankDiameter", g.ShankDiameter),
                Number("overallLength", g.OverallLength)));

            CuttingData c = tool.Cutting;
            if (c != null)
            {
                element.Add(new XElement(
                    "cutting",
                    Number("spindleRpm", c.SpindleRpm),
                    Number("cuttingFeed", c.CuttingFeed),
                    Number("plungeFeed", c.PlungeFeed),
                    Number("stepover", c.Stepover),
                    Number("stepdown", c.Stepdown),
                    Number("entryFeed", c.EntryFeed),
                    Number("exitFeed", c.ExitFeed),
                    Number("rampFeed", c.RampFeed),
                    Number("retractFeed", c.RetractFeed),
                    Number("rampSpindleRpm", c.RampSpindleRpm),
                    new XAttribute("spindleClockwise", c.SpindleClockwise),
                    new XAttribute("feedMode", c.FeedMode.ToString()),
                    new XAttribute("coolant", c.Coolant.ToString())));
            }

            MachineData m = tool.Machine;
            if (m != null)
            {
                element.Add(new XElement(
                    "machine",
                    new XAttribute("diameterOffset", m.DiameterOffset),
                    new XAttribute("lengthOffset", m.LengthOffset),
                    new XAttribute("turret", m.Turret),
                    new XAttribute("breakControl", m.BreakControl),
                    new XAttribute("manualToolChange", m.ManualToolChange)));
            }

            if (tool.Extra != null && tool.Extra.Count > 0)
            {
                element.Add(new XElement(
                    "extra",
                    tool.Extra.Select(kv => new XElement(
                        "item",
                        new XAttribute("key", kv.Key),
                        new XAttribute("value", kv.Value ?? string.Empty)))));
            }

            return element;
        }

        private static void AddIfPresent(XElement element, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                element.Add(new XAttribute(name, value));
            }
        }

        /// <summary>
        /// Invariant culture, so a file written on a comma-decimal machine still loads
        /// on a dot-decimal one. "R" round-trips without losing precision.
        /// </summary>
        private static XAttribute Number(string name, double value)
        {
            return new XAttribute(name, value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
