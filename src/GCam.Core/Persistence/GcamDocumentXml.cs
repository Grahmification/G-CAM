using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;

namespace GCam.Core.Persistence
{
    /// <summary>
    /// Names and numbers that make a stored document readable by a later build.
    /// </summary>
    public static class GcamDocumentFormat
    {
        public const string RootElement = "gcamDocument";

        /// <summary>
        /// Bumped when a change cannot be read by the previous build. Adding an element or
        /// an attribute is not such a change: everything reads with a fallback.
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>The stream inside the part's storage holding this XML.</summary>
        public const string ModelStreamName = "model.xml";

        /// <summary>What a toolpath stream is called: this, then a number.</summary>
        /// <remarks>
        /// **Toolpath streams cannot be named after their operation.** Structured storage
        /// caps an element name around 31 characters and a 36-character GUID does not fit,
        /// so streams are numbered and the XML records which operation owns which. See
        /// docs/decisions/0009-persist-toolpaths-in-the-document.md.
        /// </remarks>
        public const string ToolpathStreamPrefix = "tp";

        public static string ToolpathStreamName(int index) =>
            ToolpathStreamPrefix + index.ToString("0000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Turns a <see cref="JobDocument"/> into the XML stored inside a part, and back.
    /// </summary>
    /// <remarks>
    /// In Core, with no SOLIDWORKS anywhere near it, so the format can be round-tripped by
    /// a headless test. `GCam.SolidWorks` owns getting the bytes into and out of the
    /// document's third-party storage; everything about what those bytes mean is here.
    ///
    /// **Reading never throws over one bad operation.** A file written by a newer build, an
    /// operation whose strategy this build does not have, a parameter that will not parse -
    /// all of them are reported and skipped, because losing one operation is survivable and
    /// losing the whole part's CAM data is not. The exception is a file that is not a G-CAM
    /// document at all, or is newer wholesale, which is worth stopping for.
    /// </remarks>
    public sealed class GcamDocumentXml
    {
        private readonly StrategyCatalog _catalog;

        public GcamDocumentXml(StrategyCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        // ---- writing ----------------------------------------------------------------

        public void Write(JobDocument document, Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            new XDocument(new XDeclaration("1.0", "utf-8", null), WriteElement(document))
                .Save(stream);
        }

        public XElement WriteElement(JobDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var root = new XElement(
                GcamDocumentFormat.RootElement,
                new XAttribute("version", GcamDocumentFormat.CurrentVersion),
                new XAttribute("units", "mm"));

            root.Add(WriteTools(document));

            // The toolpath stream each operation's result lives in. Assigned while writing
            // so the numbering is dense however many operations have come and gone.
            var streams = new Dictionary<string, int>(StringComparer.Ordinal);
            int next = 1;

            var jobs = new XElement("jobs");
            foreach (Job job in document.Jobs)
            {
                jobs.Add(WriteJob(job, document, streams, ref next));
            }

            root.Add(jobs);

            if (document.DefaultJob != null)
            {
                root.Add(new XAttribute("defaultJob", document.DefaultJob.Id ?? string.Empty));
            }

            return root;
        }

        /// <summary>
        /// Which toolpath stream each operation's result belongs in, in the same order
        /// <see cref="WriteElement"/> assigns them.
        /// </summary>
        /// <remarks>
        /// The storage layer needs this to know what to write where, and it has to agree
        /// with the XML exactly - so both come from one walk of the document rather than
        /// two that could drift.
        /// </remarks>
        public static IReadOnlyList<ToolpathStreamEntry> ToolpathStreams(JobDocument document)
        {
            var entries = new List<ToolpathStreamEntry>();

            if (document == null)
            {
                return entries;
            }

            int next = 1;
            foreach (Operation operation in document.Jobs.SelectMany(j => j.Operations))
            {
                if (operation?.Toolpath != null && !operation.Toolpath.IsEmpty)
                {
                    entries.Add(new ToolpathStreamEntry(
                        operation, GcamDocumentFormat.ToolpathStreamName(next++)));
                }
            }

            return entries;
        }

        private static XElement WriteTools(JobDocument document)
        {
            // The part's tools are written in exactly the shape a .gcamtools library uses,
            // through the same writer, so one tool cannot read back two ways.
            var library = new ToolLibrary { Id = "part", Name = "Part tools" };
            library.Tools.AddRange(document.Tools);
            library.Holders.AddRange(
                document.Tools
                    .Select(t => t.Holder)
                    .Where(h => h != null && !string.IsNullOrEmpty(h.Id))
                    .GroupBy(h => h.Id, StringComparer.Ordinal)
                    .Select(g => g.First()));

            return new XElement("tools", new GcamXmlLibraryWriter().WriteElement(library));
        }

        private XElement WriteJob(
            Job job, JobDocument document, IDictionary<string, int> streams, ref int next)
        {
            var element = new XElement(
                "job",
                new XAttribute("id", job.Id ?? string.Empty),
                new XAttribute("name", job.Name ?? string.Empty),
                new XAttribute("workOffset", job.WorkOffset));

            if (job.CoordinateSystem != null && !job.CoordinateSystem.IsEmpty)
            {
                element.Add(WriteGeometryRef(job.CoordinateSystem, "coordinateSystem"));
            }

            element.Add(new XElement(
                "bodies",
                job.ModelBodies.Where(b => b != null).Select(b => WriteGeometryRef(b, "body"))));

            element.Add(WriteStock(job.Stock));
            element.Add(WriteExtra(job.Extra));

            var operations = new XElement("operations");
            foreach (Operation operation in job.Operations)
            {
                operations.Add(WriteOperation(operation, streams, ref next));
            }

            element.Add(operations);
            return element;
        }

        private static XElement WriteStock(Stock stock)
        {
            stock = stock ?? new Stock();

            return new XElement(
                "stock",
                new XAttribute("mode", stock.Mode.ToString()),
                Number("topOffset", stock.TopOffset),
                Number("bottomOffset", stock.BottomOffset),
                Number("sideOffset", stock.SideOffset),
                Number("offsetX", stock.OffsetX),
                Number("offsetY", stock.OffsetY),
                Number("width", stock.Width),
                Number("depth", stock.Depth),
                Number("height", stock.Height));
        }

        private XElement WriteOperation(
            Operation operation, IDictionary<string, int> streams, ref int next)
        {
            var element = new XElement(
                "operation",
                new XAttribute("id", operation.Id ?? string.Empty),
                new XAttribute("name", operation.Name ?? string.Empty),
                new XAttribute("strategy", operation.Strategy.Value),
                new XAttribute("enabled", operation.Enabled),
                new XAttribute("state", operation.State.ToString()),
                Number("tolerance", operation.Tolerance));

            if (!string.IsNullOrEmpty(operation.Comment))
            {
                element.Add(new XAttribute("comment", operation.Comment));
            }

            if (!string.IsNullOrEmpty(operation.ToolId))
            {
                element.Add(new XAttribute("toolId", operation.ToolId));
            }

            if (!string.IsNullOrEmpty(operation.StateMessage))
            {
                element.Add(new XAttribute("stateMessage", operation.StateMessage));
            }

            if (operation.Toolpath != null && !operation.Toolpath.IsEmpty)
            {
                int index = next++;
                streams[operation.Id ?? string.Empty] = index;
                element.Add(new XAttribute(
                    "toolpathStream", GcamDocumentFormat.ToolpathStreamName(index)));
            }

            element.Add(WriteCutting(operation.Cutting));
            element.Add(WriteHeights(operation.Heights));
            element.Add(new XElement(
                "frame",
                new XAttribute("inherit", operation.Frame.InheritFromJob),
                WriteGeometryRef(operation.Frame.CoordinateSystem, "coordinateSystem")));

            element.Add(WriteSettings(operation.Settings));
            element.Add(WriteExtra(operation.Extra));

            return element;
        }

        private static XElement WriteSettings(StrategySettings settings)
        {
            var bag = new ParameterBag();
            settings.WriteParameters(bag);

            var element = new XElement(
                "settings",
                bag.Values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new XElement(
                    "parameter",
                    new XAttribute("name", v.Key),
                    new XAttribute("value", v.Value ?? string.Empty))));

            if (settings is IContourSelectionOwner owner && owner.Contours.Count > 0)
            {
                element.Add(new XElement("contours", owner.Contours.Select(WriteContour)));
            }

            return element;
        }

        private static XElement WriteContour(ContourSelection selection)
        {
            return new XElement(
                "contour",
                new XAttribute("propagateTangent", selection.PropagateTangent),
                new XAttribute("propagateAlongZ", selection.PropagateAlongZ),
                new XAttribute("reversed", selection.Reversed),
                WriteGeometryRef(selection.Entity));
        }

        private static XElement WriteGeometryRef(GeometryRef entity, string elementName = "ref")
        {
            if (entity == null)
            {
                return null;
            }

            return new XElement(
                elementName,
                new XAttribute("id", entity.PersistentId ?? string.Empty),
                new XAttribute("kind", entity.Kind.ToString()),
                entity.DisplayName == null ? null : new XAttribute("name", entity.DisplayName));
        }

        private static XElement WriteCutting(CuttingData cutting)
        {
            cutting = cutting ?? new CuttingData();

            return new XElement(
                "cutting",
                Number("spindleRpm", cutting.SpindleRpm),
                Number("rampSpindleRpm", cutting.RampSpindleRpm),
                new XAttribute("clockwise", cutting.SpindleClockwise),
                new XAttribute("feedMode", cutting.FeedMode.ToString()),
                new XAttribute("coolant", cutting.Coolant.ToString()),
                Number("cuttingFeed", cutting.CuttingFeed),
                Number("plungeFeed", cutting.PlungeFeed),
                Number("entryFeed", cutting.EntryFeed),
                Number("exitFeed", cutting.ExitFeed),
                Number("rampFeed", cutting.RampFeed),
                Number("retractFeed", cutting.RetractFeed),
                Number("stepover", cutting.Stepover),
                Number("stepdown", cutting.Stepdown));
        }

        private static XElement WriteHeights(OperationHeights heights)
        {
            heights = heights ?? new OperationHeights();

            return new XElement(
                "heights",
                WriteHeight("clearance", heights.Clearance),
                WriteHeight("retract", heights.Retract),
                WriteHeight("feed", heights.Feed),
                WriteHeight("top", heights.Top),
                WriteHeight("bottom", heights.Bottom));
        }

        private static XElement WriteHeight(string name, HeightSetting height)
        {
            return new XElement(
                name,
                new XAttribute("mode", height.Mode.ToString()),
                Number("offset", height.Offset),
                WriteGeometryRef(height.Reference));
        }

        private static XElement WriteExtra(IDictionary<string, string> extra)
        {
            if (extra == null || extra.Count == 0)
            {
                return null;
            }

            return new XElement(
                "extra",
                extra.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new XElement(
                    "item",
                    new XAttribute("key", e.Key),
                    new XAttribute("value", e.Value ?? string.Empty))));
        }

        // ---- reading ----------------------------------------------------------------

        public DocumentReadResult Read(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            XDocument parsed;
            try
            {
                parsed = XDocument.Load(stream);
            }
            catch (XmlException ex)
            {
                throw new GCamUserException(
                    "The CAM data stored in this part is not valid XML. " +
                    "It cannot be recovered, and the part will open without its jobs.", ex);
            }

            return ReadElement(parsed.Root);
        }

        public DocumentReadResult ReadElement(XElement root)
        {
            if (root == null || root.Name.LocalName != GcamDocumentFormat.RootElement)
            {
                throw new GCamUserException("This part does not contain G-CAM data.");
            }

            int version = ReadInt(root, "version", 1);
            if (version > GcamDocumentFormat.CurrentVersion)
            {
                throw new GCamUserException(
                    "This part's CAM data was written by a newer version of G-CAM " +
                    $"(format {version}, this build understands {GcamDocumentFormat.CurrentVersion}). " +
                    "Opening it here could lose data, so it has not been loaded.");
            }

            var document = new JobDocument();
            var problems = new List<string>();
            var toolpaths = new Dictionary<string, Operation>(StringComparer.Ordinal);

            ReadTools(root, document, problems);

            foreach (XElement element in root.Elements("jobs").Elements("job"))
            {
                Job job = ReadJob(element, problems, toolpaths);
                document.Add(job);
            }

            string defaultJobId = ReadString(root, "defaultJob");
            Job defaultJob = document.FindById(defaultJobId);
            if (defaultJob != null)
            {
                document.MakeDefault(defaultJob);
            }

            return new DocumentReadResult(document, problems, toolpaths);
        }

        private static void ReadTools(XElement root, JobDocument document, ICollection<string> problems)
        {
            XElement libraryElement = root
                .Elements("tools")
                .Elements(GcamXmlLibrary.RootElement)
                .FirstOrDefault();

            if (libraryElement == null)
            {
                return;
            }

            try
            {
                ToolLibraryReadResult result =
                    new GcamXmlLibraryReader().ReadElement(libraryElement, "this part");

                foreach (Tool tool in result.Library.Tools)
                {
                    document.AddTool(tool);
                }
            }
            catch (GCamUserException ex)
            {
                // Without tools the operations cannot cut, but their parameters and
                // geometry are still worth having back.
                problems.Add("The part's tool list could not be read: " + ex.Message);
            }
        }

        private Job ReadJob(
            XElement element, ICollection<string> problems, IDictionary<string, Operation> toolpaths)
        {
            var job = new Job
            {
                Id = ReadString(element, "id") ?? Guid.NewGuid().ToString("D"),
                Name = ReadString(element, "name"),
                CoordinateSystem = ReadGeometryRefOrName(
                    element.Element("coordinateSystem"),
                    ReadString(element, "coordinateSystem"),
                    GeometryRefKind.CoordinateSystem),
                WorkOffset = ReadInt(element, "workOffset", WorkOffsets.First),
            };

            job.ModelBodies.AddRange(
                element.Elements("bodies").Elements("body")
                    .Select(b => ReadGeometryRefOrName(b, null, GeometryRefKind.Body))
                    .Where(b => b != null));

            job.Stock = ReadStock(element.Element("stock"));
            ReadExtra(element.Element("extra"), job.Extra);

            foreach (XElement child in element.Elements("operations").Elements("operation"))
            {
                Operation operation = ReadOperation(child, problems, toolpaths);
                if (operation != null)
                {
                    job.Operations.Add(operation);
                }
            }

            return job;
        }

        private static Stock ReadStock(XElement element)
        {
            var stock = new Stock();

            if (element == null)
            {
                return stock;
            }

            stock.Mode = ReadEnum(element, "mode", StockMode.RelativeBox);
            stock.TopOffset = ReadDouble(element, "topOffset");
            stock.BottomOffset = ReadDouble(element, "bottomOffset");
            stock.SideOffset = ReadDouble(element, "sideOffset");
            stock.OffsetX = ReadDouble(element, "offsetX");
            stock.OffsetY = ReadDouble(element, "offsetY");
            stock.Width = ReadDouble(element, "width");
            stock.Depth = ReadDouble(element, "depth");
            stock.Height = ReadDouble(element, "height");

            return stock;
        }

        private Operation ReadOperation(
            XElement element, ICollection<string> problems, IDictionary<string, Operation> toolpaths)
        {
            var strategy = new StrategyId(ReadString(element, "strategy"));
            string name = ReadString(element, "name") ?? "Operation";

            if (!_catalog.TryGet(strategy, out StrategyDescriptor descriptor))
            {
                // Skipping one operation beats refusing the whole part. Saying which, and
                // why, is what stops it looking like the data simply vanished.
                problems.Add(
                    $"Operation '{name}' uses a '{strategy}' strategy this build does not " +
                    "have, and has been left out.");
                return null;
            }

            StrategySettings settings = descriptor.CreateSettings();
            ReadSettings(element.Element("settings"), settings);

            var operation = new Operation(settings)
            {
                Id = ReadString(element, "id") ?? Guid.NewGuid().ToString("D"),
                Name = name,
                Comment = ReadString(element, "comment"),
                Enabled = ReadBool(element, "enabled", true),
                ToolId = ReadString(element, "toolId"),
                Tolerance = ReadDouble(element, "tolerance", Operation.DefaultTolerance),
                State = ReadEnum(element, "state", OperationState.NotGenerated),
                StateMessage = ReadString(element, "stateMessage"),
            };

            operation.Cutting = ReadCutting(element.Element("cutting"));
            operation.Heights = ReadHeights(element.Element("heights"));
            operation.Frame = ReadFrame(element.Element("frame"));
            ReadExtra(element.Element("extra"), operation.Extra);

            // An operation that was mid-generation when the part was saved is not mid
            // anything now.
            if (operation.State == OperationState.Generating)
            {
                operation.State = OperationState.NotGenerated;
            }

            string stream = ReadString(element, "toolpathStream");
            if (!string.IsNullOrEmpty(stream))
            {
                toolpaths[stream] = operation;
            }
            else if (operation.State == OperationState.Generated
                     || operation.State == OperationState.Stale
                     || operation.State == OperationState.Warning)
            {
                // The state claims a toolpath that was never stored.
                operation.State = OperationState.NotGenerated;
            }

            return operation;
        }

        private static void ReadSettings(XElement element, StrategySettings settings)
        {
            if (element == null)
            {
                return;
            }

            var bag = new ParameterBag(
                element.Elements("parameter")
                    .Select(p => new KeyValuePair<string, string>(
                        ReadString(p, "name") ?? string.Empty, ReadString(p, "value")))
                    .Where(p => p.Key.Length > 0));

            settings.ReadParameters(bag);

            if (settings is IContourSelectionOwner owner)
            {
                owner.Contours.Clear();
                owner.Contours.AddRange(
                    element.Elements("contours").Elements("contour").Select(ReadContour));
            }
        }

        private static ContourSelection ReadContour(XElement element)
        {
            return new ContourSelection(ReadGeometryRef(element.Element("ref")))
            {
                PropagateTangent = ReadBool(element, "propagateTangent", true),
                PropagateAlongZ = ReadBool(element, "propagateAlongZ"),
                Reversed = ReadBool(element, "reversed"),
            };
        }

        /// <summary>
        /// Reads a reference that may have been written before references existed.
        /// </summary>
        /// <remarks>
        /// Parts saved before 2026-09-13 stored bodies and coordinate systems as bare
        /// names, so a reference read from one has a display name and no persistent id.
        /// That is a usable state: the SOLIDWORKS side falls back to selecting by name and
        /// stamps the id in as soon as it resolves one, so an old part migrates the first
        /// time it is opened and saved.
        /// </remarks>
        private static GeometryRef ReadGeometryRefOrName(
            XElement element, string legacyName, GeometryRefKind kind)
        {
            GeometryRef reference = ReadGeometryRef(element);

            if (reference != null)
            {
                if (reference.Kind == GeometryRefKind.Unknown)
                {
                    reference.Kind = kind;
                }

                return reference;
            }

            // A <body name="Boss-Extrude1"/> from before, or coordinateSystem="..." as an
            // attribute on the job rather than an element under it.
            string name = ReadString(element, "name") ?? legacyName;

            return string.IsNullOrWhiteSpace(name)
                ? null
                : new GeometryRef { Kind = kind, DisplayName = name };
        }

        private static GeometryRef ReadGeometryRef(XElement element)
        {
            if (element == null)
            {
                return null;
            }

            return new GeometryRef
            {
                PersistentId = ReadString(element, "id"),
                Kind = ReadEnum(element, "kind", GeometryRefKind.Unknown),
                DisplayName = ReadString(element, "name"),
            };
        }

        private static CuttingData ReadCutting(XElement element)
        {
            var cutting = new CuttingData();

            if (element == null)
            {
                return cutting;
            }

            cutting.SpindleRpm = ReadDouble(element, "spindleRpm");
            cutting.RampSpindleRpm = ReadDouble(element, "rampSpindleRpm");
            cutting.SpindleClockwise = ReadBool(element, "clockwise", true);
            cutting.FeedMode = ReadEnum(element, "feedMode", FeedMode.PerMinute);
            cutting.Coolant = ReadEnum(element, "coolant", CoolantMode.Flood);
            cutting.CuttingFeed = ReadDouble(element, "cuttingFeed");
            cutting.PlungeFeed = ReadDouble(element, "plungeFeed");
            cutting.EntryFeed = ReadDouble(element, "entryFeed");
            cutting.ExitFeed = ReadDouble(element, "exitFeed");
            cutting.RampFeed = ReadDouble(element, "rampFeed");
            cutting.RetractFeed = ReadDouble(element, "retractFeed");
            cutting.Stepover = ReadDouble(element, "stepover");
            cutting.Stepdown = ReadDouble(element, "stepdown");

            return cutting;
        }

        private static OperationHeights ReadHeights(XElement element)
        {
            var heights = new OperationHeights();

            if (element == null)
            {
                return heights;
            }

            heights.Clearance = ReadHeight(element.Element("clearance"), heights.Clearance);
            heights.Retract = ReadHeight(element.Element("retract"), heights.Retract);
            heights.Feed = ReadHeight(element.Element("feed"), heights.Feed);
            heights.Top = ReadHeight(element.Element("top"), heights.Top);
            heights.Bottom = ReadHeight(element.Element("bottom"), heights.Bottom);

            return heights;
        }

        private static HeightSetting ReadHeight(XElement element, HeightSetting fallback)
        {
            if (element == null)
            {
                return fallback;
            }

            return new HeightSetting(
                ReadEnum(element, "mode", fallback.Mode),
                ReadDouble(element, "offset"))
            {
                Reference = ReadGeometryRef(element.Element("ref")),
            };
        }

        private static OperationFrame ReadFrame(XElement element)
        {
            var frame = new OperationFrame();

            if (element == null)
            {
                return frame;
            }

            frame.InheritFromJob = ReadBool(element, "inherit", true);
            frame.CoordinateSystem = ReadGeometryRefOrName(
                element.Element("coordinateSystem"),
                ReadString(element, "coordinateSystem"),
                GeometryRefKind.CoordinateSystem);

            return frame;
        }

        private static void ReadExtra(XElement element, IDictionary<string, string> into)
        {
            foreach (XElement item in element?.Elements("item") ?? Enumerable.Empty<XElement>())
            {
                string key = ReadString(item, "key");
                if (!string.IsNullOrEmpty(key))
                {
                    into[key] = ReadString(item, "value");
                }
            }
        }

        // ---- primitives --------------------------------------------------------------

        private static XAttribute Number(string name, double value) =>
            new XAttribute(name, value.ToString("R", CultureInfo.InvariantCulture));

        private static string ReadString(XElement element, string name) =>
            element?.Attribute(name)?.Value;

        private static double ReadDouble(XElement element, string name, double fallback = 0)
        {
            string raw = ReadString(element, name);

            return double.TryParse(
                raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : fallback;
        }

        private static int ReadInt(XElement element, string name, int fallback)
        {
            string raw = ReadString(element, name);

            return int.TryParse(
                raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        private static bool ReadBool(XElement element, string name, bool fallback = false)
        {
            string raw = ReadString(element, name);

            return bool.TryParse(raw, out bool value) ? value : fallback;
        }

        private static TEnum ReadEnum<TEnum>(XElement element, string name, TEnum fallback)
            where TEnum : struct
        {
            string raw = ReadString(element, name);

            return Enum.TryParse(raw, true, out TEnum value) ? value : fallback;
        }
    }

    /// <summary>Which stream an operation's toolpath belongs in.</summary>
    public sealed class ToolpathStreamEntry
    {
        public ToolpathStreamEntry(Operation operation, string streamName)
        {
            Operation = operation;
            StreamName = streamName;
        }

        public Operation Operation { get; }

        public string StreamName { get; }
    }

    /// <summary>
    /// A document read back out of a part, and anything that went wrong doing it.
    /// </summary>
    public sealed class DocumentReadResult
    {
        public DocumentReadResult(
            JobDocument document,
            IReadOnlyList<string> problems,
            IReadOnlyDictionary<string, Operation> toolpathStreams)
        {
            Document = document;
            Problems = problems ?? new string[0];
            ToolpathStreams = toolpathStreams ?? new Dictionary<string, Operation>();
        }

        public JobDocument Document { get; }

        /// <summary>
        /// What was skipped or could not be read. Empty on a clean load. Worth showing
        /// once, not per operation.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>
        /// Stream name to the operation waiting for it, for the storage layer to fill in.
        /// </summary>
        public IReadOnlyDictionary<string, Operation> ToolpathStreams { get; }
    }
}
