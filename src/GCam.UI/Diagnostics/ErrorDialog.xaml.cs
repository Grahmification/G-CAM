using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace GCam.UI.Diagnostics
{
    /// <summary>
    /// Error window: a plain summary, optional copyable detail, and a route to the logs.
    /// </summary>
    public partial class ErrorDialog : Window
    {
        private readonly string _details;
        private readonly string _logDirectory;

        private ErrorDialog(string summary, string details, string logDirectory)
        {
            InitializeComponent();

            _details = details;
            _logDirectory = logDirectory;

            SummaryText.Text = summary;

            if (string.IsNullOrEmpty(details))
            {
                // Expected failure: the message is the whole story.
                DetailsExpander.Visibility = Visibility.Collapsed;
                CopyButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                DetailsText.Text = details;
            }

            if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory))
            {
                LogFolderButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Reports a defect, with the full exception available to copy.</summary>
        public static ErrorDialog ForException(Exception ex, string context, string logDirectory)
        {
            return new ErrorDialog(
                "G-CAM hit an unexpected problem and the operation was stopped." + Environment.NewLine +
                "The details below have been written to the log.",
                BuildReport(ex, context),
                logDirectory);
        }

        /// <summary>Reports an expected problem in the user's own terms.</summary>
        public static ErrorDialog ForUserMessage(string message, string logDirectory)
        {
            return new ErrorDialog(message, null, logDirectory);
        }

        /// <summary>
        /// The text a colleague pastes into a bug report. Includes inner exceptions,
        /// which are usually where the real cause is.
        /// </summary>
        private static string BuildReport(Exception ex, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Context : " + context);
            sb.AppendLine("Time    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Version : " + typeof(ErrorDialog).Assembly.GetName().Version);
            sb.AppendLine();

            for (Exception current = ex; current != null; current = current.InnerException)
            {
                sb.AppendLine(current.GetType().FullName + ": " + current.Message);
                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    sb.AppendLine(current.StackTrace);
                }

                if (current.InnerException != null)
                {
                    sb.AppendLine("--- caused by ---");
                }
            }

            return sb.ToString();
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_details);
                CopyButton.Content = "Copied";
            }
            catch (Exception)
            {
                // The clipboard is occasionally locked by another process. Not worth
                // raising a second error dialog over - the text is selectable anyway.
                CopyButton.Content = "Copy failed";
            }
        }

        private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", "\"" + _logDirectory + "\"");
            }
            catch (Exception)
            {
                LogFolderButton.IsEnabled = false;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
