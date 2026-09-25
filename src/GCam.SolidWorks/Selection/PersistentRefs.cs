using System;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Selection
{
    /// <summary>
    /// Turns model entities into references that survive a save, and back again.
    /// </summary>
    /// <remarks>
    /// A job outlives the session that made it, so naming a body is not enough: rename it
    /// and the job silently cuts something else. `GetPersistReference3` gives SOLIDWORKS'
    /// own identity for an entity, which survives renaming, and which is what
    /// <see cref="GeometryRef.PersistentId"/> holds.
    ///
    /// The id comes back as a byte array and is stored base64, so the XML stays text.
    ///
    /// <b>The name is kept as well, and is the fallback.</b> Parts saved before this change
    /// have names and no ids; <see cref="CurrentName"/> falls back to the stored name and
    /// stamps the id in as soon as it resolves one, so an old part migrates itself the
    /// first time it is opened and saved.
    /// </remarks>
    internal static class PersistentRefs
    {
        /// <summary>
        /// SOLIDWORKS' identity for an entity, base64 encoded, or null if it will not
        /// give one.
        /// </summary>
        public static string Capture(ModelDoc2 model, object entity)
        {
            if (model == null || entity == null)
            {
                return null;
            }

            try
            {
                var id = model.Extension.GetPersistReference3(entity) as byte[];

                return id == null || id.Length == 0 ? null : Convert.ToBase64String(id);
            }
            catch (Exception)
            {
                // Not every object has one. A missing id is survivable - the name still
                // identifies it - so this is not worth failing an edit over.
                return null;
            }
        }

        /// <summary>
        /// The entity an id points at, or null when the part no longer has it.
        /// </summary>
        public static object Resolve(ModelDoc2 model, string persistentId)
        {
            if (model == null || string.IsNullOrWhiteSpace(persistentId))
            {
                return null;
            }

            byte[] id;
            try
            {
                id = Convert.FromBase64String(persistentId);
            }
            catch (FormatException)
            {
                return null;
            }

            try
            {
                object found = model.Extension.GetObjectByPersistReference3(id, out int error);

                // Anything but zero means the object is gone, replaced, or ambiguous. The
                // caller falls back to the name, which is the best that can be done.
                return error == 0 ? found : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A reference to an entity, carrying both its identity and its name.
        /// </summary>
        public static GeometryRef Describe(
            ModelDoc2 model, object entity, GeometryRefKind kind, string name)
        {
            return new GeometryRef
            {
                PersistentId = Capture(model, entity),
                Kind = kind,
                DisplayName = name,
            };
        }

        /// <summary>
        /// What the referenced entity is called in the part *now*, or null if it has gone.
        /// </summary>
        /// <remarks>
        /// The one place the migration happens. Resolving by id gives the current name even
        /// after a rename, and updates the stored name to match; resolving by name - the
        /// only option for a part saved before ids - stamps the id in so the next save does
        /// not need the fallback.
        ///
        /// <b>This mutates the reference</b>, on purpose and idempotently. Anything that
        /// resolves a reference therefore also migrates it, which is why an old part comes
        /// out modern the first time it is saved for any reason.
        /// </remarks>
        public static string CurrentName(
            ModelDoc2 model, GeometryRef reference, Func<string, object> findByName, IGCamLog log = null)
        {
            if (model == null || reference == null || reference.IsEmpty)
            {
                return null;
            }

            object entity = Resolve(model, reference.PersistentId);

            if (entity != null)
            {
                string name = NameOf(entity);

                if (!string.IsNullOrEmpty(name) && name != reference.DisplayName)
                {
                    log?.Debug(
                        "'{0}' has been renamed to '{1}'; the job follows it.",
                        reference.DisplayName, name);
                    reference.DisplayName = name;
                }

                return name ?? reference.DisplayName;
            }

            if (reference.HasPersistentId)
            {
                // The id was good once and no longer resolves: the entity is gone. Falling
                // back to the name here would be wrong - a new body could have taken it.
                return null;
            }

            // No id at all, so this came from a part saved before references.
            object byName = findByName?.Invoke(reference.DisplayName);
            if (byName == null)
            {
                return null;
            }

            reference.PersistentId = Capture(model, byName);
            return reference.DisplayName;
        }

        /// <summary>What SOLIDWORKS calls an object, for the kinds a job refers to.</summary>
        public static string NameOf(object entity)
        {
            var body = entity as Body2;
            if (body != null)
            {
                return body.Name;
            }

            var feature = entity as Feature;
            if (feature != null)
            {
                return feature.Name;
            }

            return null;
        }
    }
}
