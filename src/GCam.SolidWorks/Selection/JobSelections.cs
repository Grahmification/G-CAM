using System;
using System.Collections.Generic;
using GCam.Core.Model;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.SolidWorks.Selection
{
    /// <summary>
    /// Reading what the user picked into a PropertyManager page's selection boxes, and
    /// putting a saved selection back.
    /// </summary>
    /// <remarks>
    /// Selections are read through ISelectionMgr rather than
    /// IPropertyManagerPageSelectionbox::GetSelectedItems, because the selection manager
    /// is the route that reports each item's *mark* - the number identifying which box
    /// it belongs to. A page with two boxes cannot tell its selections apart otherwise.
    ///
    /// Selections are stored as <see cref="GeometryRef"/>s carrying SOLIDWORKS' own
    /// persistent identity, because a job outlives the session that made it and a renamed
    /// body must not silently change what it cuts. The name travels along for the UI and
    /// as the fallback for parts saved before references existed - see
    /// <see cref="PersistentRefs"/>.
    ///
    /// SOLIDWORKS is still addressed *by name* throughout, because SelectByID2 is the only
    /// route into a PropertyManager selection box. The reference is what decides which
    /// name to use now.
    /// </remarks>
    internal static class JobSelections
    {
        /// <summary>
        /// The names of everything currently selected into the box with this mark.
        /// </summary>
        public static List<GeometryRef> RefsWithMark(
            ModelDoc2 model, int mark, GeometryRefKind kind)
        {
            var refs = new List<GeometryRef>();
            if (model == null)
            {
                return refs;
            }

            var selection = model.SelectionManager as SelectionMgr;
            if (selection == null)
            {
                return refs;
            }

            int count = selection.GetSelectedObjectCount2(mark);

            // One-based, and asking for a different mark than the count was taken with
            // silently renumbers things - so the mark is passed to both calls.
            for (int i = 1; i <= count; i++)
            {
                object entity = selection.GetSelectedObject6(i, mark);
                string name = PersistentRefs.NameOf(entity);

                if (!string.IsNullOrEmpty(name))
                {
                    refs.Add(PersistentRefs.Describe(model, entity, kind, name));
                }
            }

            return refs;
        }

        /// <summary>
        /// Selects the named bodies into the box with this mark, skipping any that are
        /// no longer in the part.
        /// </summary>
        /// <returns>The names that could not be found.</returns>
        public static List<string> SelectBodies(
            ModelDoc2 model, IEnumerable<GeometryRef> bodies, int mark)
        {
            var missing = new List<string>();
            if (model == null || bodies == null)
            {
                return missing;
            }

            var extension = model.Extension;

            foreach (GeometryRef reference in bodies)
            {
                // The reference decides which name to ask for, which is what makes a
                // renamed body still select.
                string name = PersistentRefs.CurrentName(
                    model, reference, n => FindSolidBody(model, n));

                if (string.IsNullOrEmpty(name))
                {
                    missing.Add(reference?.DisplayName ?? "(unnamed)");
                    continue;
                }

                // SOLIDWORKS addresses a body by name plus type through SelectByID2. The
                // empty strings are the unused callout/config arguments.
                bool selected = extension.SelectByID2(
                    name, "SOLIDBODY", 0, 0, 0, true, mark, null,
                    (int)swSelectOption_e.swSelectOptionDefault);

                if (!selected)
                {
                    missing.Add(name);
                }
            }

            return missing;
        }

        /// <summary>The solid body with this name, or null. The migration fallback.</summary>
        private static Body2 FindSolidBody(ModelDoc2 model, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var part = model as PartDoc;
            var bodies = part?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];

            if (bodies == null)
            {
                return null;
            }

            foreach (object item in bodies)
            {
                var body = item as Body2;
                if (body != null && string.Equals(body.Name, name, StringComparison.Ordinal))
                {
                    return body;
                }
            }

            return null;
        }

        /// <summary>
        /// Selects a coordinate system feature by name into the box with this mark.
        /// </summary>
        /// <returns>False when the part no longer has that coordinate system.</returns>
        public static bool SelectCoordinateSystem(
            ModelDoc2 model, GeometryRef reference, int mark)
        {
            if (model == null || reference == null || reference.IsEmpty)
            {
                return true;
            }

            string name = PersistentRefs.CurrentName(
                model, reference, n => FindFeature(model, n, "CoordSys"));

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return model.Extension.SelectByID2(
                name, "COORDSYS", 0, 0, 0, true, mark, null,
                (int)swSelectOption_e.swSelectOptionDefault);
        }

        /// <summary>
        /// What the job's coordinate system is called now, or null if it has gone.
        /// </summary>
        public static string CurrentCoordinateSystemName(ModelDoc2 model, GeometryRef reference)
        {
            return PersistentRefs.CurrentName(
                model, reference, n => FindFeature(model, n, "CoordSys"));
        }

        /// <summary>The named feature of a given type, or null. The migration fallback.</summary>
        private static Feature FindFeature(ModelDoc2 model, string name, string typeName)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var feature = model.FirstFeature() as Feature;

            while (feature != null)
            {
                if (string.Equals(feature.Name, name, StringComparison.Ordinal)
                    && string.Equals(feature.GetTypeName2(), typeName, StringComparison.Ordinal))
                {
                    return feature;
                }

                feature = feature.GetNextFeature() as Feature;
            }

            return null;
        }

        /// <summary>
        /// Every solid body in the part, by name. What a job machines when no bodies
        /// have been chosen.
        /// </summary>
        public static List<string> AllSolidBodyNames(ModelDoc2 model)
        {
            var names = new List<string>();

            var part = model as PartDoc;
            if (part == null)
            {
                return names;
            }

            // false: visible bodies only would hide work the user can still see in the
            // tree, so ask for everything and let the job's own selection narrow it.
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null)
            {
                return names;
            }

            foreach (object item in bodies)
            {
                var body = item as Body2;
                if (body != null)
                {
                    names.Add(body.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// The bodies a job machines: the ones it names, or every solid body when it
        /// names none.
        /// </summary>
        /// <remarks>
        /// The objects rather than the names, for code that has to measure them. Job's
        /// "empty means every solid body" rule is applied here rather than by the caller,
        /// so it cannot be honoured by one caller and forgotten by another.
        ///
        /// Names the part no longer has are skipped in silence. Whoever is about to use
        /// this has already reported the mismatch where the user could act on it - see
        /// JobPropertyPage.RestoreSelections - and saying it again on every repaint helps
        /// nobody.
        /// </remarks>
        public static List<Body2> SolidBodies(
            ModelDoc2 model, IReadOnlyCollection<GeometryRef> bodyRefs)
        {
            var chosen = new List<Body2>();

            var part = model as PartDoc;
            if (part == null)
            {
                return chosen;
            }

            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null)
            {
                return chosen;
            }

            bool all = bodyRefs == null || bodyRefs.Count == 0;

            HashSet<string> wanted = null;
            if (!all)
            {
                wanted = new HashSet<string>(StringComparer.Ordinal);

                foreach (GeometryRef reference in bodyRefs)
                {
                    string name = PersistentRefs.CurrentName(
                        model, reference, n => FindSolidBody(model, n));

                    if (!string.IsNullOrEmpty(name))
                    {
                        wanted.Add(name);
                    }
                }
            }

            foreach (object item in bodies)
            {
                var body = item as Body2;

                if (body != null && (all || wanted.Contains(body.Name)))
                {
                    chosen.Add(body);
                }
            }

            return chosen;
        }

        private static string NameOf(object selected)
        {
            var body = selected as Body2;
            if (body != null)
            {
                return body.Name;
            }

            var feature = selected as Feature;
            if (feature != null)
            {
                return feature.Name;
            }

            return null;
        }
    }
}
