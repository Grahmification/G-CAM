using SolidWorks.Interop.swpublished;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GCam.AddIn
{
    /// <summary>
    /// Portion of the class for registering the addin with SolidWorks
    /// </summary>
    [ComVisible(true)]
    [Guid("df725bf7-4ceb-4425-8931-a072139ddd01")]
    [DisplayName("G-CAM")]
    [Description("CAM Addin for SolidWorks")]
    public partial class GCamAddin : ISwAddin
    {
        private const string ADDIN_KEY_TEMPLATE = @"SOFTWARE\SolidWorks\Addins\{{{0}}}";
        private const string ADDIN_STARTUP_KEY_TEMPLATE = @"Software\SolidWorks\AddInsStartup\{{{0}}}";
        private const string ADD_IN_TITLE_REG_KEY_NAME = "Title";
        private const string ADD_IN_DESCRIPTION_REG_KEY_NAME = "Description";

        /// <summary>
        /// Registers the addin with SolidWorks via the Windows Registry
        /// </summary>
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                var addInTitle = "";
                var loadAtStartup = true;
                var addInDesc = "";

                var dispNameAtt = t.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();

                if (dispNameAtt != null)
                {
                    addInTitle = dispNameAtt.DisplayName;
                }
                else
                {
                    addInTitle = t.ToString();
                }

                var descAtt = t.GetCustomAttributes(false).OfType<DescriptionAttribute>().FirstOrDefault();

                if (descAtt != null)
                {
                    addInDesc = descAtt.Description;
                }
                else
                {
                    addInDesc = t.ToString();
                }

                var addInkey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    string.Format(ADDIN_KEY_TEMPLATE, t.GUID));

                addInkey.SetValue(null, 0);

                addInkey.SetValue(ADD_IN_TITLE_REG_KEY_NAME, addInTitle);
                addInkey.SetValue(ADD_IN_DESCRIPTION_REG_KEY_NAME, addInDesc);

                var addInStartupkey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    string.Format(ADDIN_STARTUP_KEY_TEMPLATE, t.GUID));

                addInStartupkey.SetValue(null, Convert.ToInt32(loadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure("register", ex);
            }
        }

        /// <summary>
        /// Unregisters the addin with SolidWorks via the Windows Registry
        /// </summary>
        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(
                    string.Format(ADDIN_KEY_TEMPLATE, t.GUID));

                Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(
                    string.Format(ADDIN_STARTUP_KEY_TEMPLATE, t.GUID));
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure("unregister", ex);
            }
        }

        /// <summary>
        /// Entry point 3 reporting. These run inside regasm.exe, not SOLIDWORKS, so
        /// there is no Serilog and no WPF here - only the console regasm is showing
        /// and a file to read afterwards.
        /// </summary>
        private static void ReportRegistrationFailure(string action, Exception ex)
        {
            Console.WriteLine("G-CAM: failed to " + action + " the add-in: " + ex.Message);

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "G-CAM", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "registration.log"),
                    DateTime.Now.ToString("u") + "  " + action + Environment.NewLine +
                    ex + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
                // The console message above is all we can offer.
            }
        }
    }
}
