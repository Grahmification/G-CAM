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
    /// Imports HSMWorks / Fusion 360 tool libraries (.hsmlib).
    /// </summary>
    /// <remarks>
    /// Format notes worth knowing, all confirmed against a real Tormach library:
    ///
    /// * The document is namespaced (http://www.hsmworks.com/xml/2004/cnc/tool-library),
    ///   so element lookups must be namespace-qualified.
    /// * Units are declared PER TOOL, not per file, and may be "millimeters" or "inches".
    /// * A tool carries two numbers: the <c>id</c> attribute is a library index, while
    ///   <c>nc/@number</c> is the carousel position the machine uses. The latter is what
    ///   becomes <see cref="Tool.Number"/>.
    /// * <c>taper-angle</c> means different things by tool type - see
    ///   <see cref="IncludedTipAngle"/>.
    /// * Holder sections are a cumulative profile from the tool end upward, where a
    ///   section with length 0 is an instantaneous step in diameter.
    /// * The same holder guid repeats across tools, so holders are de-duplicated and
    ///   shared, matching G-CAM's model.
    /// </remarks>
    public sealed class HsmLibraryReader : IToolLibraryReader
    {
        private static readonly XNamespace Ns = "http://www.hsmworks.com/xml/2004/cnc/tool-library";

        public string FormatName => "HSMWorks / Fusion tool library";

        public string FileExtension => ".hsmlib";

        /// <summary>
        /// HSM tool type names mapped to G-CAM types. Anything absent is reported as a
        /// skipped tool rather than guessed at - substituting a drill for a tap would
        /// wreck a part, and the substitution would be invisible afterwards.
        /// </summary>
        private static readonly Dictionary<string, ToolType> TypeMap =
            new Dictionary<string, ToolType>(StringComparer.OrdinalIgnoreCase)
            {
                { "flat end mill", ToolType.FlatEndMill },
                { "ball end mill", ToolType.BallEndMill },
                { "bull nose end mill", ToolType.BullNoseEndMill },
                { "radius mill", ToolType.BullNoseEndMill },
                { "chamfer mill", ToolType.ChamferMill },
                { "spot drill", ToolType.SpotDrill },
                { "center drill", ToolType.SpotDrill },
                { "drill", ToolType.Drill },
                { "tap right hand", ToolType.Tap },
                { "tap left hand", ToolType.Tap },
            };

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
                throw new GCamUserException($"'{Describe(sourcePath)}' is not valid XML: {ex.Message}", ex);
            }

            XElement root = document.Root;
            if (root == null || root.Name.LocalName != "tool-table")
            {
                throw new GCamUserException(
                    $"'{Describe(sourcePath)}' is not an HSMWorks tool library " +
                    "(expected a <tool-table> root element).");
            }

            var library = new ToolLibrary
            {
                Name = Path.GetFileNameWithoutExtension(sourcePath ?? "Imported tools"),
                SourcePath = sourcePath,
            };

            var warnings = new List<string>();
            var holdersByGuid = new Dictionary<string, Holder>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement toolElement in root.Elements(Ns + "tool"))
            {
                try
                {
                    Tool tool = ReadTool(toolElement, library, holdersByGuid, warnings);
                    if (tool != null)
                    {
                        library.Tools.Add(tool);
                    }
                }
                catch (GCamUserException ex)
                {
                    // One bad tool must not lose the rest of the library.
                    warnings.Add($"Skipped '{DescribeTool(toolElement)}' - {ex.Message}");
                }
            }

            return new ToolLibraryReadResult(library, warnings);
        }

        private Tool ReadTool(
            XElement element,
            ToolLibrary library,
            Dictionary<string, Holder> holdersByGuid,
            List<string> warnings)
        {
            string rawType = (string)element.Attribute("type");
            ToolType type;
            if (!TypeMap.TryGetValue(rawType ?? string.Empty, out type))
            {
                warnings.Add(
                    $"Skipped '{DescribeTool(element)}' - tool type '{rawType}' is not supported yet.");
                return null;
            }

            double scale = UnitScale(element);

            var tool = new Tool
            {
                // Reuse HSM's guid as our identity, so re-importing the same file updates
                // tools rather than duplicating them.
                Id = NormaliseGuid((string)element.Attribute("guid")) ?? Guid.NewGuid().ToString("D"),
                Type = type,
                Name = Text(element, "description"),
                Comment = Text(element, "comment"),
                Manufacturer = Text(element, "manufacturer"),
                ProductId = Text(element, "product-id"),
                Material = (string)element.Element(Ns + "material")?.Attribute("name"),
            };

            ReadNc(element, tool);
            ReadBody(element, tool, type, scale);
            ReadMotion(element, tool, scale);
            ReadCoolant(element, tool);

            XElement holderElement = element.Element(Ns + "holder");
            if (holderElement != null)
            {
                tool.Holder = ResolveHolder(holderElement, library, holdersByGuid, scale);
            }

            // Keep what we could not map, rather than destroying it on import.
            Remember(tool.Extra, "hsm.id", (string)element.Attribute("id"));
            Remember(tool.Extra, "hsm.type", rawType);
            Remember(tool.Extra, "hsm.tool-version", (string)element.Attribute("version"));

            IReadOnlyList<string> problems = tool.Validate();
            if (problems.Count > 0)
            {
                throw new GCamUserException(string.Join(" ", problems));
            }

            return tool;
        }

        private static void ReadNc(XElement element, Tool tool)
        {
            XElement nc = element.Element(Ns + "nc");
            if (nc == null)
            {
                return;
            }

            // The carousel number, not the library index in the tool's id attribute.
            tool.Number = Int(nc, "number", 0);
            tool.Machine = new MachineData
            {
                DiameterOffset = Int(nc, "diameter-offset", 0),
                LengthOffset = Int(nc, "length-offset", 0),
                Turret = Int(nc, "turret", 0),
                BreakControl = Flag(nc, "break-control"),
                ManualToolChange = Flag(nc, "manual-tool-change"),
            };
        }

        private static void ReadBody(XElement element, Tool tool, ToolType type, double scale)
        {
            XElement body = element.Element(Ns + "body");
            if (body == null)
            {
                throw new GCamUserException("it has no <body> element.");
            }

            double rawTaper = Number(body, "taper-angle");

            tool.Geometry = new ToolGeometry
            {
                Diameter = Number(body, "diameter") * scale,
                CornerRadius = Number(body, "corner-radius") * scale,
                TipDiameter = Number(body, "tip-diameter") * scale,
                FluteLength = Number(body, "flute-length") * scale,
                ShoulderLength = Number(body, "shoulder-length") * scale,
                BodyLength = Number(body, "body-length") * scale,
                ShankDiameter = Number(body, "shaft-diameter") * scale,
                OverallLength = Number(body, "overall-length") * scale,
                ThreadPitch = Number(body, "thread-pitch") * scale,
                FluteCount = Int(body, "number-of-flutes", 0),

                // Angles are degrees whatever the length units.
                TipAngle = IncludedTipAngle(type, rawTaper),
                SecondTipAngle = IncludedTipAngle(type, Number(body, "taper-angle2")),
                ThreadProfileAngle = Number(body, "thread-profile-angle"),
            };

            // A ball nose is recorded by type, not by corner radius, so fill it in.
            if (type == ToolType.BallEndMill && tool.Geometry.CornerRadius <= 0)
            {
                tool.Geometry.CornerRadius = tool.Geometry.Diameter / 2.0;
            }
        }

        /// <summary>
        /// Converts HSM's taper angle to an included angle.
        /// </summary>
        /// <remarks>
        /// HSM stores both meanings under the same attribute name, and the difference
        /// is a factor of two in the cutter's shape.
        ///
        /// Verified against a real library: a "1/4 Chamfer Mill" has diameter 6.35,
        /// tip-diameter 0, taper-angle 45 and flute-length 3.175. Treating 45 as a
        /// HALF angle gives a cone height of 3.175 / tan(45) = 3.175 - exactly the
        /// flute length. Treating it as an included angle gives 7.67, which contradicts
        /// the tool's own flute length. Physically too: a 45 degree chamfer mill cuts a
        /// 45 degree chamfer, which is 45 degrees from the axis.
        ///
        /// Drills go the other way: taper-angle 118 is the standard included point
        /// angle, and a 2.5mm drill at 118 included gives a 0.75mm point height, which
        /// is correct. As a half angle it would be meaningless.
        /// </remarks>
        private static double IncludedTipAngle(ToolType type, double hsmTaperAngle)
        {
            if (hsmTaperAngle <= 0)
            {
                return 0;
            }

            return type == ToolType.ChamferMill ? hsmTaperAngle * 2.0 : hsmTaperAngle;
        }

        private static void ReadMotion(XElement element, Tool tool, double scale)
        {
            XElement motion = element.Element(Ns + "motion");
            if (motion == null)
            {
                return;
            }

            tool.Cutting = new CuttingData
            {
                SpindleRpm = Number(motion, "spindle-rpm"),
                RampSpindleRpm = Number(motion, "ramp-spindle-rpm"),
                SpindleClockwise = !string.Equals(
                    (string)motion.Attribute("clockwise"), "no", StringComparison.OrdinalIgnoreCase),
                FeedMode = string.Equals(
                    (string)motion.Attribute("feed-mode"), "per-revolution", StringComparison.OrdinalIgnoreCase)
                    ? FeedMode.PerRevolution
                    : FeedMode.PerMinute,

                // Feeds are a length per unit time, so they scale with the tool's units.
                CuttingFeed = Number(motion, "cutting-feedrate") * scale,
                PlungeFeed = Number(motion, "plunge-feedrate") * scale,
                EntryFeed = Number(motion, "entry-feedrate") * scale,
                ExitFeed = Number(motion, "exit-feedrate") * scale,
                RampFeed = Number(motion, "ramp-feedrate") * scale,
                RetractFeed = Number(motion, "retract-feedrate") * scale,

                // HSM keeps stepover and stepdown on the operation, not the tool, so
                // these stay zero and must be set before the tool is usable.
                Stepover = 0,
                Stepdown = 0,
            };
        }

        private static void ReadCoolant(XElement element, Tool tool)
        {
            string mode = (string)element.Element(Ns + "coolant")?.Attribute("mode");
            if (string.IsNullOrWhiteSpace(mode))
            {
                return;
            }

            switch (mode.Trim().ToLowerInvariant())
            {
                case "disabled":
                case "off":
                    tool.Cutting.Coolant = CoolantMode.Off;
                    break;
                case "mist":
                    tool.Cutting.Coolant = CoolantMode.Mist;
                    break;
                case "through tool":
                case "through-tool":
                    tool.Cutting.Coolant = CoolantMode.ThroughTool;
                    break;
                default:
                    tool.Cutting.Coolant = CoolantMode.Flood;
                    Remember(tool.Extra, "hsm.coolant", mode);
                    break;
            }
        }

        /// <summary>
        /// Finds or creates the holder for this tool. The same holder guid appears on
        /// many tools in an HSM file; G-CAM stores one and shares it.
        /// </summary>
        private static Holder ResolveHolder(
            XElement element, ToolLibrary library, Dictionary<string, Holder> byGuid, double scale)
        {
            string guid = NormaliseGuid((string)element.Attribute("guid"));
            Holder existing;
            if (!string.IsNullOrEmpty(guid) && byGuid.TryGetValue(guid, out existing))
            {
                return existing;
            }

            var holder = new Holder
            {
                Id = guid ?? Guid.NewGuid().ToString("D"),
                Name = (string)element.Attribute("description"),
                Comment = (string)element.Attribute("comment"),
                Vendor = (string)element.Attribute("vendor"),
                ProductId = (string)element.Attribute("product-id"),
                Segments = ReadHolderSections(element, scale),
            };

            Remember(holder.Extra, "hsm.library-name", (string)element.Attribute("library-name"));

            if (!string.IsNullOrEmpty(guid))
            {
                byGuid[guid] = holder;
            }

            library.Holders.Add(holder);
            return holder;
        }

        /// <summary>
        /// Converts HSM's cumulative section list into G-CAM's segment list.
        /// </summary>
        /// <remarks>
        /// Each &lt;section&gt; gives a diameter and the axial length over which the
        /// profile reaches it. A length of 0 is a step - the diameter changes with no
        /// height - which G-CAM represents by carrying the new diameter into the next
        /// real segment rather than storing a zero-length stage.
        /// </remarks>
        private static List<HolderSegment> ReadHolderSections(XElement element, double scale)
        {
            var segments = new List<HolderSegment>();
            double current = double.NaN;

            foreach (XElement section in element.Elements(Ns + "section"))
            {
                double diameter = Number(section, "diameter") * scale;
                double length = Number(section, "length") * scale;

                if (double.IsNaN(current))
                {
                    current = diameter;
                    if (length <= 0)
                    {
                        continue;
                    }
                }

                if (length <= 0)
                {
                    current = diameter;
                    continue;
                }

                segments.Add(new HolderSegment(length, current, diameter));
                current = diameter;
            }

            return segments;
        }

        private static double UnitScale(XElement element)
        {
            string unit = ((string)element.Attribute("unit") ?? "millimeters").Trim().ToLowerInvariant();
            switch (unit)
            {
                case "inches":
                case "inch":
                case "in":
                    return Units.MillimetresPerInch;
                default:
                    return 1.0;
            }
        }

        private static void Remember(IDictionary<string, string> extra, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                extra[key] = value;
            }
        }

        private static string NormaliseGuid(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // HSM wraps guids in braces; strip them so ids look consistent in G-CAM.
            return raw.Trim().Trim('{', '}');
        }

        private static string Text(XElement element, string childName)
        {
            string value = (string)element.Element(Ns + childName);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string DescribeTool(XElement element)
        {
            string description = Text(element, "description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            return "tool id " + ((string)element.Attribute("id") ?? "?");
        }

        private static string Describe(string sourcePath)
        {
            return string.IsNullOrEmpty(sourcePath) ? "the tool library" : Path.GetFileName(sourcePath);
        }

        private static double Number(XElement element, string attribute)
        {
            string raw = (string)element.Attribute(attribute);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }

            double value;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static int Int(XElement element, string attribute, int fallback)
        {
            string raw = (string)element.Attribute(attribute);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            int value;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static bool Flag(XElement element, string attribute)
        {
            string raw = ((string)element.Attribute(attribute) ?? string.Empty).Trim();
            return raw == "1" || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
