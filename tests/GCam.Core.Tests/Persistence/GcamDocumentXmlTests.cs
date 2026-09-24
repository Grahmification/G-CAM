using System.IO;
using System.Linq;
using System.Xml.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Geometry.Primitives;
using GCam.Core.Model;
using GCam.Core.Model.Heights;
using GCam.Core.Persistence;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Strategies.Shared;
using GCam.Core.Tooling;
using Xunit;

namespace GCam.Core.Tests.Persistence
{
    public class GcamDocumentXmlTests
    {
        private static GcamDocumentXml Xml() => new GcamDocumentXml(StrategyCatalog.CreateDefault());

        private static JobDocument Sample()
        {
            var document = new JobDocument();

            Tool tool = document.AddTool(new Tool
            {
                Id = "tool-4",
                Number = 4,
                Name = "Aluminium",
                Type = ToolType.FlatEndMill,
                SourceLibraryId = "lib-1",
                Geometry = { Diameter = 12.7, FluteLength = 31.75, FluteCount = 3 },
                Cutting = { SpindleRpm = 7500, CuttingFeed = 1800, PlungeFeed = 600 },
            });

            Job job = document.AddNew();
            job.Name = "Roughing";
            job.CoordinateSystem = new GeometryRef
            {
                PersistentId = "cs-persist-id",
                Kind = GeometryRefKind.CoordinateSystem,
                DisplayName = "Coordinate System1",
            };
            job.WorkOffset = 2;
            job.ModelBodies.Add(new GeometryRef
            {
                PersistentId = "body-persist-id",
                Kind = GeometryRefKind.Body,
                DisplayName = "Boss-Extrude1",
            });
            job.Stock.SideOffset = 2;
            job.Stock.TopOffset = 1;
            job.Extra["hsm.origin"] = "imported";

            var settings = new Contour2dSettings
            {
                Direction = CutDirection.Conventional,
                StockToLeave = 0.2,
                VerticalStockToLeave = 0.05,
                LeadOutMatchesLeadIn = false,
            };
            settings.MultipleDepths.Enabled = true;
            settings.MultipleDepths.MaximumStepdown = 2.5;
            settings.MultipleDepths.UseEvenStepdowns = false;
            settings.LeadIn.Radius = 3;
            settings.LeadOut.Radius = 4;
            settings.LeadOut.Sweep = 45;
            settings.Contours.Add(new ContourSelection(new GeometryRef
            {
                PersistentId = "edge-base64==",
                Kind = GeometryRefKind.Edge,
                DisplayName = "Edge1",
            })
            {
                PropagateTangent = false,
                PropagateAlongZ = true,
                Reversed = true,
            });

            var operation = new Operation(settings)
            {
                Name = "2D Contour1",
                Comment = "Finish pass",
                Tolerance = 0.005,
                State = OperationState.Generated,
                Toolpath = new Toolpath()
                    .Add(Move.Rapid(new Vec3(0, 0, 10)))
                    .Add(Move.Cut(new Vec3(10, 0, -1), 1800)),
            };
            operation.UseTool(tool);
            operation.Cutting.CuttingFeed = 1500;
            operation.Heights.Bottom = new HeightSetting(HeightMode.FromSelection, -0.5)
            {
                Reference = new GeometryRef { PersistentId = "face-1", DisplayName = "Floor" },
            };
            operation.Extra["hsm.unknown"] = "kept";

            job.Operations.Add(operation);
            return document;
        }

        private static JobDocument RoundTrip(JobDocument document)
        {
            GcamDocumentXml xml = Xml();

            using (var stream = new MemoryStream())
            {
                xml.Write(document, stream);
                stream.Position = 0;
                return xml.Read(stream).Document;
            }
        }

        [Fact]
        public void A_job_survives_a_round_trip()
        {
            Job job = RoundTrip(Sample()).Jobs.Single();

            Assert.Equal("Roughing", job.Name);
            Assert.Equal("cs-persist-id", job.CoordinateSystem.PersistentId);
            Assert.Equal("Coordinate System1", job.CoordinateSystem.DisplayName);
            Assert.Equal(2, job.WorkOffset);
            Assert.Equal("body-persist-id", job.ModelBodies.Single().PersistentId);
            Assert.Equal("Boss-Extrude1", job.ModelBodies.Single().DisplayName);
            Assert.Equal(2, job.Stock.SideOffset, 9);
            Assert.Equal(1, job.Stock.TopOffset, 9);
            Assert.Equal("imported", job.Extra["hsm.origin"]);
        }

        [Fact]
        public void The_part_tool_list_survives_with_its_library_origin()
        {
            Tool tool = RoundTrip(Sample()).Tools.Single();

            Assert.Equal("tool-4", tool.Id);
            Assert.Equal(4, tool.Number);
            Assert.Equal("lib-1", tool.SourceLibraryId);
            Assert.Equal(12.7, tool.Geometry.Diameter, 9);
            Assert.Equal(3, tool.Geometry.FluteCount);
        }

        [Fact]
        public void An_operation_keeps_its_identity_tool_and_feeds()
        {
            Operation operation = RoundTrip(Sample()).Jobs.Single().Operations.Single();

            Assert.Equal("2D Contour1", operation.Name);
            Assert.Equal("Finish pass", operation.Comment);
            Assert.Equal("tool-4", operation.ToolId);
            Assert.Equal(0.005, operation.Tolerance, 9);
            Assert.True(operation.Enabled);
            Assert.Equal("kept", operation.Extra["hsm.unknown"]);

            // The operation's own feeds, not the tool's defaults.
            Assert.Equal(1500, operation.Cutting.CuttingFeed, 9);
            Assert.Equal(7500, operation.Cutting.SpindleRpm, 9);
        }

        [Fact]
        public void Heights_keep_their_mode_offset_and_reference()
        {
            OperationHeights heights =
                RoundTrip(Sample()).Jobs.Single().Operations.Single().Heights;

            Assert.Equal(HeightMode.FromRetract, heights.Clearance.Mode);
            Assert.Equal(5, heights.Clearance.Offset, 9);

            Assert.Equal(HeightMode.FromSelection, heights.Bottom.Mode);
            Assert.Equal(-0.5, heights.Bottom.Offset, 9);
            Assert.Equal("face-1", heights.Bottom.Reference.PersistentId);
            Assert.Equal("Floor", heights.Bottom.Reference.DisplayName);
        }

        [Fact]
        public void A_height_measured_from_the_contour_round_trips()
        {
            JobDocument document = Sample();
            document.Jobs.Single().Operations.Single().Heights.Top =
                new HeightSetting(HeightMode.FromContour, 1.5);

            HeightSetting top = RoundTrip(document).Jobs.Single().Operations.Single().Heights.Top;

            Assert.Equal(HeightMode.FromContour, top.Mode);
            Assert.Equal(1.5, top.Offset, 9);
        }

        [Fact]
        public void A_height_mode_this_build_does_not_know_falls_back_to_the_default()
        {
            // What a build from before FromContour does with a file that uses it - and
            // what this one will do with whatever mode comes next. The load goes on.
            GcamDocumentXml xml = Xml();
            XElement root = xml.WriteElement(Sample());
            XElement top = root.Descendants("heights").Single().Element("top");

            top.SetAttributeValue("mode", "FromSomethingNewer");
            top.SetAttributeValue("offset", "4");

            HeightSetting read = xml.ReadElement(root)
                .Document.Jobs.Single().Operations.Single().Heights.Top;

            Assert.Equal(new OperationHeights().Top.Mode, read.Mode);
            Assert.Equal(4, read.Offset, 9);
        }

        [Fact]
        public void Strategy_parameters_survive()
        {
            var settings = (Contour2dSettings)RoundTrip(Sample())
                .Jobs.Single().Operations.Single().Settings;

            Assert.Equal(CutDirection.Conventional, settings.Direction);
            Assert.Equal(0.2, settings.StockToLeave, 9);
            Assert.Equal(0.05, settings.VerticalStockToLeave, 9);
            Assert.True(settings.MultipleDepths.Enabled);
            Assert.Equal(2.5, settings.MultipleDepths.MaximumStepdown, 9);
            Assert.False(settings.MultipleDepths.UseEvenStepdowns);
            Assert.False(settings.LeadOutMatchesLeadIn);
            Assert.Equal(3, settings.LeadIn.Radius, 9);
            Assert.Equal(4, settings.LeadOut.Radius, 9);
            Assert.Equal(45, settings.LeadOut.Sweep, 9);
        }

        [Fact]
        public void Contour_selections_keep_their_references_and_modifiers()
        {
            var settings = (Contour2dSettings)RoundTrip(Sample())
                .Jobs.Single().Operations.Single().Settings;

            ContourSelection selection = settings.Contours.Single();

            Assert.Equal("edge-base64==", selection.Entity.PersistentId);
            Assert.Equal(GeometryRefKind.Edge, selection.Entity.Kind);
            Assert.Equal("Edge1", selection.Entity.DisplayName);
            Assert.False(selection.PropagateTangent);
            Assert.True(selection.PropagateAlongZ);
            Assert.True(selection.Reversed);
        }

        [Fact]
        public void The_default_job_is_remembered()
        {
            var document = new JobDocument();
            document.AddNew();
            Job second = document.AddNew();
            document.MakeDefault(second);

            JobDocument read = RoundTrip(document);

            Assert.Equal(second.Id, read.DefaultJob.Id);
        }

        [Fact]
        public void An_operation_with_a_toolpath_names_the_stream_it_lives_in()
        {
            GcamDocumentXml xml = Xml();
            XElement root = xml.WriteElement(Sample());

            string stream = root.Descendants("operation").Single().Attribute("toolpathStream").Value;

            Assert.Equal("tp0001", stream);
        }

        [Fact]
        public void Stream_names_are_short_enough_for_structured_storage()
        {
            // A 36-character operation GUID does not fit; the element name cap is 31.
            for (int i = 1; i < 10000; i += 1111)
            {
                Assert.True(GcamDocumentFormat.ToolpathStreamName(i).Length <= 31);
            }

            Assert.True(GcamDocumentFormat.ModelStreamName.Length < 30);
        }

        [Fact]
        public void The_stream_map_matches_what_the_xml_says()
        {
            // The storage layer writes from this list and the reader looks up by what the
            // XML says. Two walks that disagreed would put a toolpath on the wrong
            // operation.
            JobDocument document = Sample();

            var entries = GcamDocumentXml.ToolpathStreams(document);
            XElement root = Xml().WriteElement(document);

            foreach (ToolpathStreamEntry entry in entries)
            {
                XElement element = root.Descendants("operation")
                    .Single(e => e.Attribute("id").Value == entry.Operation.Id);

                Assert.Equal(entry.StreamName, element.Attribute("toolpathStream").Value);
            }

            Assert.Single(entries);
        }

        [Fact]
        public void Operations_waiting_for_a_toolpath_are_reported_by_stream_name()
        {
            GcamDocumentXml xml = Xml();

            using (var stream = new MemoryStream())
            {
                xml.Write(Sample(), stream);
                stream.Position = 0;

                DocumentReadResult result = xml.Read(stream);

                Assert.Equal("2D Contour1", result.ToolpathStreams["tp0001"].Name);
            }
        }

        [Fact]
        public void An_operation_with_no_stored_path_does_not_claim_to_be_generated()
        {
            // The state would otherwise promise a toolpath that is not there, and the
            // operation would draw nothing while calling itself up to date.
            var document = new JobDocument();
            Job job = document.AddNew();
            job.Operations.Add(new Operation(new Contour2dSettings())
            {
                Name = "Claims a path",
                State = OperationState.Generated,
            });

            Operation read = RoundTrip(document).Jobs.Single().Operations.Single();

            Assert.Equal(OperationState.NotGenerated, read.State);
        }

        [Fact]
        public void An_operation_saved_mid_generation_does_not_come_back_generating()
        {
            // Nothing is generating it now, and "Generating" is the one state the UI
            // cannot clear on its own.
            var document = new JobDocument();
            Job job = document.AddNew();
            job.Operations.Add(new Operation(new Contour2dSettings())
            {
                Name = "Interrupted",
                State = OperationState.Generating,
            });

            Assert.Equal(
                OperationState.NotGenerated,
                RoundTrip(document).Jobs.Single().Operations.Single().State);
        }

        [Fact]
        public void A_stale_state_is_kept_because_the_path_is_kept()
        {
            JobDocument document = Sample();
            document.Jobs.Single().Operations.Single().State = OperationState.Stale;

            Assert.Equal(
                OperationState.Stale,
                RoundTrip(document).Jobs.Single().Operations.Single().State);
        }

        [Fact]
        public void An_operation_whose_strategy_is_unknown_is_skipped_and_reported()
        {
            // A part written by a newer build. Losing one operation is survivable; losing
            // the part's whole CAM data is not.
            GcamDocumentXml xml = Xml();
            XElement root = xml.WriteElement(Sample());
            root.Descendants("operation").Single().SetAttributeValue("strategy", "swarf5d");

            DocumentReadResult result = xml.ReadElement(root);

            Assert.Empty(result.Document.Jobs.Single().Operations);
            Assert.Contains("swarf5d", Assert.Single(result.Problems));
            Assert.Single(result.Document.Tools);
        }

        [Fact]
        public void A_document_from_a_newer_format_is_refused_rather_than_half_read()
        {
            GcamDocumentXml xml = Xml();
            XElement root = xml.WriteElement(Sample());
            root.SetAttributeValue("version", GcamDocumentFormat.CurrentVersion + 1);

            var error = Assert.Throws<GCamUserException>(() => xml.ReadElement(root));

            Assert.Contains("newer version", error.Message);
        }

        [Fact]
        public void Something_that_is_not_a_G_CAM_document_is_refused()
        {
            Assert.Throws<GCamUserException>(
                () => Xml().ReadElement(new XElement("somethingElse")));

            using (var stream = new MemoryStream(new byte[] { 60, 62, 60 }))
            {
                Assert.Throws<GCamUserException>(() => Xml().Read(stream));
            }
        }

        [Fact]
        public void An_unknown_parameter_does_not_stop_the_rest_being_read()
        {
            GcamDocumentXml xml = Xml();
            XElement root = xml.WriteElement(Sample());
            XElement settings = root.Descendants("settings").Single();

            settings.Add(new XElement(
                "parameter",
                new XAttribute("name", "somethingFromTheFuture"),
                new XAttribute("value", "42")));
            settings.Elements("parameter")
                .First(p => p.Attribute("name").Value == "stockToLeave")
                .SetAttributeValue("value", "not a number");

            var read = (Contour2dSettings)xml.ReadElement(root)
                .Document.Jobs.Single().Operations.Single().Settings;

            // The bad one falls back to its default; the good ones still arrive.
            Assert.Equal(0, read.StockToLeave, 9);
            Assert.Equal(0.05, read.VerticalStockToLeave, 9);
            Assert.Equal(CutDirection.Conventional, read.Direction);
        }

        [Fact]
        public void An_empty_document_round_trips()
        {
            JobDocument read = RoundTrip(new JobDocument());

            Assert.Empty(read.Jobs);
            Assert.Empty(read.Tools);
            Assert.Null(read.DefaultJob);
        }

        [Fact]
        public void Numbers_are_written_culture_independently()
        {
            // A part saved on a machine with a decimal comma has to open on one without.
            string xml = Xml().WriteElement(Sample()).ToString();

            Assert.Contains("0.2", xml);
            Assert.DoesNotContain("0,2", xml);
        }
    }
}
