using System;

namespace GCam.Core.Model
{
    /// <summary>
    /// One machining operation inside a job.
    /// </summary>
    /// <remarks>
    /// A placeholder. It carries only enough to exist as a node in the job tree; the
    /// strategy, tool, geometry and heights arrive with the operation work. The type is
    /// here now so the tree and the job model can be built and tested against something
    /// real rather than against a gap.
    /// </remarks>
    public sealed class Operation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");

        public string Name { get; set; }

        /// <summary>Deep copy, keeping the id. For duplicating a whole job.</summary>
        public Operation Clone()
        {
            return new Operation
            {
                Id = Id,
                Name = Name,
            };
        }

        /// <summary>Deep copy with a fresh id, for a copy that stands on its own.</summary>
        public Operation CloneAsNew()
        {
            Operation copy = Clone();
            copy.Id = Guid.NewGuid().ToString("D");
            return copy;
        }

        public override string ToString() => Name ?? "Operation";
    }
}
