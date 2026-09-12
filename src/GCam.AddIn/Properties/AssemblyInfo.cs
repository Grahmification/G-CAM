using System.Runtime.InteropServices;

// Types are not COM-visible by default; GCamAddin opts in explicitly with its own
// [ComVisible(true)]. Flipping this to true would expose every public type to COM.
[assembly: ComVisible(false)]

// Typelib ID for this assembly. Carried over from the original project - changing it
// changes the assembly's COM identity, so leave it alone. The add-in's own CLSID is
// the separate Guid on GCamAddin in GCamAddinRegistration.cs.
[assembly: Guid("413ad0e7-619e-47af-aff2-198558c77e65")]
