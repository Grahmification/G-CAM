using System.Collections.Generic;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// A named collection of tools and the holders they mount in.
    /// </summary>
    /// <remarks>
    /// Read-only by design: jobs copy tools out, they never reach back in. The concrete
    /// <see cref="ToolLibrary"/> adds mutation for the library editor.
    /// </remarks>
    public interface IToolLibrary
    {
        /// <summary>Stable identity, recorded on tools copied into a job.</summary>
        string Id { get; }

        string Name { get; }

        IReadOnlyList<Tool> Tools { get; }

        IReadOnlyList<Holder> Holders { get; }

        /// <summary>The tool with this id, or null.</summary>
        Tool FindById(string toolId);

        /// <summary>
        /// The first tool with this carousel number, or null. Numbers are not enforced
        /// to be unique - a shop may keep alternatives on the same number.
        /// </summary>
        Tool FindByNumber(int number);
    }
}
