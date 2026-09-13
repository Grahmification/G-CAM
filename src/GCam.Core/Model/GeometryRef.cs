namespace GCam.Core.Model
{
    /// <summary>What kind of model entity a <see cref="GeometryRef"/> points at.</summary>
    /// <remarks>
    /// Explicit numbers: these are written into the document and must not shift when a
    /// member is added.
    /// </remarks>
    public enum GeometryRefKind
    {
        Unknown = 0,
        Face = 1,
        Edge = 2,
        Vertex = 3,
        Body = 4,
        Sketch = 5,

        /// <summary>A coordinate system feature, which is what a job's origin is.</summary>
        CoordinateSystem = 6,
    }

    /// <summary>
    /// A reference to one entity in the SOLIDWORKS model, stored so it survives a save
    /// and reopen.
    /// </summary>
    /// <remarks>
    /// Core cannot name SOLIDWORKS entities, so it holds the opaque persistent id and
    /// nothing else. <c>IModelDocExtension::GetPersistReference3</c> produces it and
    /// GCam.SolidWorks resolves it back to geometry at the edge.
    ///
    /// Persistent ids rather than names, because a renamed face must not silently change
    /// what a proven operation cuts - and a job outlives the session that made it.
    ///
    /// <see cref="DisplayName"/> is a cached label for the UI and for error messages. It
    /// is allowed to go stale - it names what the reference was called when it was picked,
    /// which is exactly what someone needs to hear when the reference no longer resolves.
    /// </remarks>
    public sealed class GeometryRef
    {
        /// <summary>The opaque persistent reference, base64 encoded.</summary>
        public string PersistentId { get; set; }

        public GeometryRefKind Kind { get; set; } = GeometryRefKind.Unknown;

        /// <summary>What to call this entity in the UI. May be stale; never authoritative.</summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// True when this identifies nothing at all - no reference and no name.
        /// </summary>
        /// <remarks>
        /// Deliberately not "has no persistent id". A reference read from a part saved
        /// before references existed carries a name and nothing else, and it identifies
        /// something perfectly well until the id can be stamped in. Treating that as empty
        /// would drop the selection on the next save - which is the opposite of the
        /// migration being careful about.
        /// </remarks>
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(PersistentId) && string.IsNullOrWhiteSpace(DisplayName);

        /// <summary>
        /// True when this can be resolved without falling back to a name lookup.
        /// </summary>
        public bool HasPersistentId => !string.IsNullOrWhiteSpace(PersistentId);

        public GeometryRef Clone() => (GeometryRef)MemberwiseClone();

        public override string ToString() =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Kind.ToString();
    }
}
