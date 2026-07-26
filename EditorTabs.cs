using System;
using System.Diagnostics;
using System.IO;
using System.Text;

// Opening a file for editing. Partial of MainWindow.
//
// ONE SEAM, deliberately. Everything that wants a file edited - the prompt script today, the
// PowerShell profile and the results menu next - goes through OpenForEditing, so when the
// editor tab lands there is one method to change and not one call site moves.
//
// Until then the file goes to notepad. Notepad rather than the shell association: a .ps1 that
// has been associated with powershell.exe RUNS when it is opened, and "edit my profile" quietly
// executing the profile instead is not a risk worth taking to save an interim a few lines.
namespace KillerFind
{
    public partial class MainWindow
    {
        // A BOM on a new file, for the same reason PromptScript.cs writes one: PowerShell 5.1
        // reads a BOM-less file as the system ANSI codepage, so every box-drawing glyph in a
        // script written without one comes back as mojibake. Only these extensions, because
        // everywhere else a BOM is noise that other tools have to step over.
        private static readonly string[] BomExtensions = { ".ps1", ".psm1", ".psd1" };

        /// <summary>Open <paramref name="path"/> for editing, creating it if it is not there.</summary>
        /// <remarks>
        /// Created rather than refused, because the file that turns out to be missing is usually
        /// $PROFILE: on a machine nobody has customized PowerShell on it does not exist at all,
        /// and an edit row that does nothing on a fresh machine has failed at the one moment it
        /// was wanted.
        /// </remarks>
        internal void OpenForEditing(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);

                if (!File.Exists(path))
                {
                    bool bom = Array.IndexOf(BomExtensions,
                                             Path.GetExtension(path).ToLowerInvariant()) >= 0;
                    File.WriteAllText(path, string.Empty, new UTF8Encoding(bom));
                }

                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"")
                              { UseShellExecute = false });
            }
            catch { /* the editor tab will report this properly; here it can only be a missing notepad */ }
        }
    }
}
