using System;
using System.Collections.Generic;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Strategies.Shared;
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
    public static class JobSelections
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

        /// <summary>
        /// The edges and faces in the box with this mark, as contour selections.
        /// </summary>
        public static List<ContourSelection> ContourSelectionsWithMark(ModelDoc2 model, int mark) =>
            ContourSelectionsFrom(model, mark);

        /// <summary>
        /// The edges and faces currently selected in the graphics area.
        /// </summary>
        /// <remarks>
        /// Mark -1 means "everything selected", rather than the numbered mark a
        /// PropertyManager box uses - this reads a plain selection made before any page is
        /// open, which is how New Operation picks up what the user had already clicked.
        /// </remarks>
        public static List<ContourSelection> CurrentContourSelections(ModelDoc2 model) =>
            ContourSelectionsFrom(model, -1);

        /// <remarks>
        /// Edges and faces have no names in SOLIDWORKS, so the display name is positional.
        /// The persistent reference is the identity; the name is only a label.
        /// </remarks>
        private static List<ContourSelection> ContourSelectionsFrom(ModelDoc2 model, int mark)
        {
            var selections = new List<ContourSelection>();

            var selection = model?.SelectionManager as SelectionMgr;
            if (selection == null)
            {
                return selections;
            }

            int count = selection.GetSelectedObjectCount2(mark);

            for (int i = 1; i <= count; i++)
            {
                object entity = selection.GetSelectedObject6(i, mark);

                GeometryRefKind kind =
                    entity is Edge ? GeometryRefKind.Edge :
                    entity is Face2 ? GeometryRefKind.Face :
                    GeometryRefKind.Unknown;

                if (kind == GeometryRefKind.Unknown)
                {
                    continue;
                }

                string label = kind + " " + (selections.Count + 1);

                selections.Add(new ContourSelection(
                    PersistentRefs.Describe(model, entity, kind, label)));
            }

            return selections;
        }

        /// <summary>
        /// Puts a stored contour back into the box with this mark.
        /// </summary>
        /// <remarks>
        /// Selected as an object rather than by name: edges and faces have no names, so
        /// SelectByID2 - which every other selection here goes through - cannot address
        /// one. The persistent reference is the only handle there is.
        ///
        /// **Cast to `IEntity`, not `Entity`.** `Select4` is declared on `IEntity`, which
        /// an edge and a face both support; `Entity` is the coclass interface and a QI for
        /// it off an `Edge` is not something the API promises. Getting that wrong fails in
        /// the most confusing way available - the reference resolves perfectly well, so
        /// generation cuts the right geometry, and only *re-selecting* it comes back empty.
        ///
        /// The three ways this can fail are logged apart, because they mean different
        /// things: a reference that no longer resolves is a model edit, an object that
        /// will not cast is a bug here, and a refused `Select4` is neither.
        /// </remarks>
        public static bool SelectContour(
            ModelDoc2 model, ContourSelection contour, int mark, IGCamLog log = null)
        {
            log = log ?? NullLog.Instance;

            if (model == null || contour == null || contour.IsEmpty)
            {
                return false;
            }

            object found = PersistentRefs.Resolve(model, contour.Entity?.PersistentId);

            if (found == null)
            {
                log.Debug("{0} no longer resolves in this part.", contour.Entity);
                return false;
            }

            var entity = found as IEntity;

            if (entity == null)
            {
                log.Warn(
                    "{0} resolved to a {1}, which cannot be selected. This is a G-CAM bug.",
                    contour.Entity, found.GetType().Name);
                return false;
            }

            var selection = model.SelectionManager as SelectionMgr;
            SelectData data = selection?.CreateSelectData();

            if (data == null)
            {
                return false;
            }

            data.Mark = mark;

            if (entity.Select4(true, data))
            {
                return true;
            }

            log.Debug("{0} resolved but SOLIDWORKS refused to select it.", contour.Entity);
            return false;
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
