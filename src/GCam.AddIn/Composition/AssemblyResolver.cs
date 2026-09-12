using System;
using System.IO;
using System.Reflection;

namespace GCam.AddIn.Composition
{
    /// <summary>
    /// Resolves G-CAM's own dependencies from the add-in folder, ignoring version.
    /// </summary>
    /// <remarks>
    /// An add-in DLL gets no app.config of its own - SOLIDWORKS owns the process and
    /// SLDWORKS.exe.config is the only configuration the CLR reads. That means the
    /// binding redirects the SDK would normally generate for us do not exist, and NuGet's
    /// version unification breaks at runtime.
    ///
    /// The concrete failure this was written for: Serilog.Sinks.File 7.0.0 is compiled
    /// against Serilog 4.2.0.0, NuGet resolved Serilog to 4.4.0.0, and with no redirect
    /// the CLR looked for 4.2.0.0 and gave up - even though 4.4.0.0 was sitting in the
    /// same folder.
    ///
    /// Assembly.LoadFrom by path ignores the requested version, so this acts as a
    /// catch-all binding redirect for anything we ship.
    ///
    /// Two rules keep this neighbourly, because AssemblyResolve is process-wide and we
    /// share the process with SOLIDWORKS and every other add-in:
    ///   * only resolve files that exist in OUR directory
    ///   * return null for everything else, so other resolvers still get their turn
    /// </remarks>
    internal static class AssemblyResolver
    {
        private static readonly object Gate = new object();
        private static bool _installed;
        private static string _probeDirectory;

        /// <summary>
        /// Registers the handler. Safe to call repeatedly.
        /// </summary>
        /// <remarks>
        /// MUST run before any method that mentions a dependency's types is called. The
        /// JIT resolves a method's type references when the method is first entered, so
        /// a try/catch INSIDE such a method never gets the chance to run - which is
        /// exactly how the Serilog failure escaped LoggingSetup.Create's own catch block.
        /// Hence the call from GCamAddin's static constructor.
        /// </remarks>
        public static void Install()
        {
            lock (Gate)
            {
                if (_installed)
                {
                    return;
                }

                _probeDirectory = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
                if (string.IsNullOrEmpty(_probeDirectory))
                {
                    return;
                }

                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                _installed = true;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);

                // Satellite resource assemblies are asked for constantly and we ship none.
                if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string candidate = Path.Combine(_probeDirectory, requested.Name + ".dll");
                if (!File.Exists(candidate))
                {
                    // Not ours. Let the CLR and any other handler carry on.
                    return null;
                }

                return Assembly.LoadFrom(candidate);
            }
            catch (Exception)
            {
                // A resolver that throws turns a missing-assembly problem into something
                // far more confusing. Declining is always safe.
                return null;
            }
        }
    }
}
