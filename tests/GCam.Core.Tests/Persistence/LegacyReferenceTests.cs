using System.Linq;
using System.Xml.Linq;
using GCam.Core.Model;
using GCam.Core.Persistence;
using GCam.Core.Strategies;
using GCam.Core.Strategies.Contour2d;
using Xunit;

namespace GCam.Core.Tests.Persistence
{
    /// <summary>
    /// Parts saved before bodies and coordinate systems became persistent references.
    /// </summary>
    /// <remarks>
    /// Those parts exist - one was saved on 2026-09-13, the day the storage layer first
    /// worked - so the reader has to keep understanding them. A reference read from one
    /// carries a name and no id, which is a usable state: the SOLIDWORKS side selects by
    /// name and stamps the id in the first time it resolves one.
    /// </remarks>
    public class LegacyReferenceTests
    {
        private static GcamDocumentXml Xml() => new GcamDocumentXml(StrategyCatalog.CreateDefault());

        /// <summary>A document in the shape the first working build wrote.</summary>
        private static XElement LegacyDocument()
        {
            return XElement.Parse(@"
<gcamDocument version='1' units='mm'>
  <jobs>
    <job id='job-1' name='Roughing' workOffset='1' coordinateSystem='Coordinate System1'>
      <bodies>
        <body name='Boss-Extrude1' />
        <body name='Cut-Extrude2' />
      </bodies>
      <stock mode='RelativeBox' sideOffset='2' />
      <operations>
        <operation id='op-1' name='2D Contour1' strategy='contour2d'
                   enabled='true' state='NotGenerated' tolerance='0.01'>
          <frame inherit='false' coordinateSystem='Coordinate System2' />
        </operation>
      </operations>
    </job>
  </jobs>
</gcamDocument>");
        }

        [Fact]
        public void Bodies_stored_as_names_still_load()
        {
            Job job = Xml().ReadElement(LegacyDocument()).Document.Jobs.Single();

            Assert.Equal(
                new[] { "Boss-Extrude1", "Cut-Extrude2" },
                job.ModelBodies.Select(b => b.DisplayName));
        }

        [Fact]
        public void A_name_only_reference_identifies_something_and_is_not_empty()
        {
            // The trap this test exists for: treating "no persistent id" as "nothing
            // selected" would drop every selection in the part on the next save.
            GeometryRef body = Xml().ReadElement(LegacyDocument())
                .Document.Jobs.Single().ModelBodies.First();

            Assert.False(body.IsEmpty);
            Assert.False(body.HasPersistentId);
            Assert.Equal(GeometryRefKind.Body, body.Kind);
        }

        [Fact]
        public void A_coordinate_system_stored_as_an_attribute_still_loads()
        {
            Job job = Xml().ReadElement(LegacyDocument()).Document.Jobs.Single();

            Assert.Equal("Coordinate System1", job.CoordinateSystem.DisplayName);
            Assert.Equal(GeometryRefKind.CoordinateSystem, job.CoordinateSystem.Kind);
            Assert.Equal("Coordinate System1", job.CoordinateSystemDisplayName);
        }

        [Fact]
        public void An_operation_frame_override_stored_as_an_attribute_still_loads()
        {
            OperationFrame frame = Xml().ReadElement(LegacyDocument())
                .Document.Jobs.Single().Operations.Single().Frame;

            Assert.False(frame.InheritFromJob);
            Assert.Equal("Coordinate System2", frame.CoordinateSystem.DisplayName);
            Assert.Equal("Coordinate System2", frame.DisplayName);
        }

        [Fact]
        public void A_legacy_document_rewrites_in_the_new_shape_without_losing_the_names()
        {
            // The migration only completes once SOLIDWORKS has stamped ids in, but a
            // save before that must not throw the names away.
            GcamDocumentXml xml = Xml();
            JobDocument document = xml.ReadElement(LegacyDocument()).Document;

            XElement written = xml.WriteElement(document);

            Assert.Equal(
                new[] { "Boss-Extrude1", "Cut-Extrude2" },
                written.Descendants("body").Select(b => b.Attribute("name").Value));

            XElement coordinateSystem = written.Descendants("job")
                .Single().Elements("coordinateSystem").Single();

            Assert.Equal("Coordinate System1", coordinateSystem.Attribute("name").Value);
        }

        [Fact]
        public void A_rewritten_legacy_document_reads_back_the_same()
        {
            GcamDocumentXml xml = Xml();

            JobDocument once = xml.ReadElement(LegacyDocument()).Document;
            JobDocument twice = xml.ReadElement(xml.WriteElement(once)).Document;

            Job job = twice.Jobs.Single();

            Assert.Equal(2, job.ModelBodies.Count);
            Assert.Equal("Coordinate System1", job.CoordinateSystem.DisplayName);
            Assert.Equal(
                "Coordinate System2",
                job.Operations.Single().Frame.CoordinateSystem.DisplayName);
        }

        [Fact]
        public void A_job_with_no_bodies_still_means_the_whole_part()
        {
            XElement legacy = LegacyDocument();
            legacy.Descendants("bodies").Single().RemoveAll();

            Job job = Xml().ReadElement(legacy).Document.Jobs.Single();

            Assert.True(job.MachinesWholePart);
            Assert.Empty(job.ModelBodies);
        }

        [Fact]
        public void A_reference_with_an_id_survives_a_round_trip_unchanged()
        {
            // The other direction: once migrated, nothing falls back to names.
            var document = new JobDocument();
            Job job = document.AddNew();
            job.ModelBodies.Add(new GeometryRef
            {
                PersistentId = "abc==",
                Kind = GeometryRefKind.Body,
                DisplayName = "Boss-Extrude1",
            });

            GcamDocumentXml xml = Xml();
            GeometryRef read = xml.ReadElement(xml.WriteElement(document))
                .Document.Jobs.Single().ModelBodies.Single();

            Assert.Equal("abc==", read.PersistentId);
            Assert.True(read.HasPersistentId);
            Assert.Equal("Boss-Extrude1", read.DisplayName);
        }
    }
}
