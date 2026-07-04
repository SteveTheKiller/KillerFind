using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace KillerFind
{
    public partial class App : Application
    {
        private const string RegKey = @"Software\KillerFind";

        // ============================================================
        // Paths (install system ported from KillerScan)
        // ============================================================

        private static readonly string AppName    = "KillerFind";
        private static readonly string ExeName    = "KillerFind.exe";
        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", AppName);
        private static readonly string InstallExe = Path.Combine(InstallDir, ExeName);

        private static readonly string StartMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
        private static readonly string StartMenuLnk = Path.Combine(StartMenuDir, $"{AppName}.lnk");
        private static readonly string DesktopLnk   = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        // ============================================================
        // Startup
        // ============================================================

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Render on the CPU so the window isn't black over console-session screen-sharing tools
            // (ScreenConnect, Kaseya LiveConnect, VNC, TeamViewer). Negligible cost for this app.
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

            // Silent install: KillerFind.exe /silent
            // Installs machine-wide to Program Files, no UI. Used by winget/choco/RMM.
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/silent", StringComparison.OrdinalIgnoreCase))
            {
                DoSilentInstall();
                Shutdown(0);
                return;
            }

            // Uninstall flag (called by Add/Remove Programs)
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                Shutdown();
                return;
            }

            // Demo / screenshot mode: KillerFind.exe --demo fills tabs with fabricated
            // results so marketing screenshots never leak real file names.
            KillerFind.MainWindow.DemoMode = Array.Exists(e.Args, a =>
                string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/demo",  StringComparison.OrdinalIgnoreCase));

            // Persist the theme/accent choice in HKCU so it survives restarts.
            Services.ThemeManager.GetSetting = GetSetting;
            Services.ThemeManager.SetSetting = SetSetting;

            // Same persistence for the chosen UI language.
            Services.LocaleManager.GetSetting = GetSetting;
            Services.LocaleManager.SetSetting = SetSetting;

            Services.ThemeManager.Initialize();    // restore saved theme before the window is built
            Services.LocaleManager.Initialize();   // then the saved language (en-US base + override)
            new MainWindow().Show();
        }

        // ============================================================
        // Preference store  (HKCU\Software\KillerFind)
        // ============================================================

        internal static string? GetSetting(string name)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RegKey);
                return k?.GetValue(name) as string;
            }
            catch { return null; }
        }

        internal static void SetSetting(string name, string value)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RegKey);
                k?.SetValue(name, value);
            }
            catch { /* best-effort */ }
        }

        // ============================================================
        // Portable badge / install (public surface used by MainWindow)
        // ============================================================

        /// <summary>True when running from outside the installed location (i.e. portable mode).</summary>
        internal static bool IsPortable()
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            return !string.Equals(currentExe, InstallExe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Installs KillerFind, then relaunches from the installed location.</summary>
        internal static void InstallAndRelaunch(bool wantDesktop)
        {
            DoInstall(wantDesktop);

            Process.Start(new ProcessStartInfo(InstallExe));
            Application.Current.Shutdown();
        }

        // ============================================================
        // Silent (machine-wide) install -- used by winget / choco / RMM
        // ============================================================

        private static void DoSilentInstall()
        {
            try
            {
                string installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
                string installExe = Path.Combine(installDir, ExeName);
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                string startMenuLnk = Path.Combine(startMenuDir, $"{AppName}.lnk");

                Directory.CreateDirectory(installDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, installExe, overwrite: true);

                Directory.CreateDirectory(startMenuDir);
                CreateShortcut(startMenuLnk, installExe);

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

                using (var key = Registry.LocalMachine.CreateSubKey(@"Software\KillerFind"))
                {
                    key.SetValue("Installed",   1);
                    key.SetValue("InstallPath", installExe);
                    key.SetValue("Version",     version);
                }

                using (var key = Registry.LocalMachine.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerFind"))
                {
                    key.SetValue("DisplayName",          AppName);
                    key.SetValue("DisplayVersion",       version);
                    key.SetValue("Publisher",            "Steve / thekiller.net");
                    key.SetValue("InstallLocation",      installDir);
                    key.SetValue("DisplayIcon",          $"{installExe},0");
                    key.SetValue("UninstallString",      $"\"{installExe}\" /uninstall");
                    key.SetValue("QuietUninstallString", $"\"{installExe}\" /uninstall");
                    key.SetValue("NoModify",             1);
                    key.SetValue("NoRepair",             1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Silent install failed: {ex.Message}");
                Environment.Exit(1);
            }
        }

        // ============================================================
        // Per-user install (the PORTABLE badge's Install button)
        // ============================================================

        private static void DoInstall(bool wantDesktop)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                string src = Process.GetCurrentProcess().MainModule!.FileName;
                File.Copy(src, InstallExe, overwrite: true);

                Directory.CreateDirectory(StartMenuDir);
                CreateShortcut(StartMenuLnk, InstallExe);
                if (wantDesktop)
                    CreateShortcut(DesktopLnk, InstallExe);

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

                using (var key = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    key.SetValue("Installed",   1);
                    key.SetValue("InstallPath", InstallExe);
                    key.SetValue("Version",     version);
                }

                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerFind"))
                {
                    key.SetValue("DisplayName",          AppName);
                    key.SetValue("DisplayVersion",       version);
                    key.SetValue("Publisher",            "Steve / thekiller.net");
                    key.SetValue("InstallLocation",      InstallDir);
                    key.SetValue("DisplayIcon",          $"{InstallExe},0");
                    key.SetValue("UninstallString",      $"\"{InstallExe}\" /uninstall");
                    key.SetValue("QuietUninstallString", $"\"{InstallExe}\" /uninstall");
                    key.SetValue("NoModify",             1);
                    key.SetValue("NoRepair",             1);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed:\n{ex.Message}", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CreateShortcut(string lnkPath, string targetPath)
        {
            // Reflection over IDispatch instead of `dynamic` - avoids needing the
            // Microsoft.CSharp runtime binder reference this project doesn't carry.
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                object shell = Activator.CreateInstance(shellType)!;
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath })!;
                var sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { targetPath });
                sc.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty,
                    null, shortcut, new object[] { Path.GetDirectoryName(targetPath)! });
                sc.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch { /* best-effort */ }
        }

        // ============================================================
        // Uninstall
        // ============================================================

        private static void Uninstall()
        {
            var res = MessageBox.Show(
                "Uninstall KillerFind from this computer?",
                $"{AppName} Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            try { File.Delete(StartMenuLnk); } catch { }
            try { Directory.Delete(StartMenuDir, recursive: false); } catch { }
            try { File.Delete(DesktopLnk); } catch { }

            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerFind"); } catch { }

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            // Self-delete: deferred via cmd batch so the EXE can exit first
            string bat = Path.Combine(Path.GetTempPath(), "killerfind_uninstall.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                $"rmdir /s /q \"{InstallDir}\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                WindowStyle     = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            MessageBox.Show("KillerFind has been uninstalled.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
