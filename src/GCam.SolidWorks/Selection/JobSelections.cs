using System;
using System.Collections.Generic;
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
    /// Everything here deals in **names**. That is a deliberate, temporary choice: jobs
    /// are not persisted yet, so a name that only has to survive the session is enough.
    /// When jobs start being written into the document these become persistent reference
    /// ids - see the note on Job.ModelBodyNames.
    /// </remarks>
    internal static class JobSelections
    {
        /// <summary>
        /// The names of everything currently selected into the box with this mark.
        /// </summary>
        public static List<string> NamesWithMark(ModelDoc2 model, int mark)
        {
            var names = new List<string>();
            if (model == null)
            {
                return names;
            }

            var selection = model.SelectionManager as SelectionMgr;
            if (selection == null)
            {
                return names;
            }

            int count = selection.GetSelectedObjectCount2(mark);

            // One-based, and asking for a different mark than the count was taken with
            // silently renumbers things - so the mark is passed to both calls.
            for (int i = 1; i <= count; i++)
            {
                string name = NameOf(selection.GetSelectedObject6(i, mark));
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        /// <summary>
        /// Selects the named bodies into the box with this mark, skipping any that are
        /// no longer in the part.
        /// </summary>
        /// <returns>The names that could not be found.</returns>
        public static List<string> SelectBodies(ModelDoc2 model, IEnumerable<string> bodyNames, int mark)
        {
            var missing = new List<string>();
            if (model == null || bodyNames == null)
            {
                return missing;
            }

            var extension = model.Extension;

            foreach (string name in bodyNames)
            {
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
        /// Selects a coordinate system feature by name into the box with this mark.
        /// </summary>
        /// <returns>False when the part no longer has that coordinate system.</returns>
        public static bool SelectCoordinateSystem(ModelDoc2 model, string name, int mark)
        {
            if (model == null || string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return model.Extension.SelectByID2(
                name, "COORDSYS", 0, 0, 0, true, mark, null,
                (int)swSelectOption_e.swSelectOptionDefault);
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
