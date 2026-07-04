using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

// KillerUI / Grunge - About overlay. Partial of MainWindow.
// MainWindow.xaml must provide an "AboutOverlay" Grid (dim bg, Collapsed, MouseLeftButtonDown=
// AboutOverlay_Click) containing a card (MouseLeftButtonDown=AboutCard_Click) with named blocks:
// AboutVersionBlock, AboutPublisherBlock, AboutThumbprintBlock, AboutSha256Block,
// AboutUpdateButton, AboutUpdateText, and a close button Click=AboutClose_Click.
namespace KillerFind
{
    public partial class MainWindow
    {
        private const string GitHubRepo     = "SteveTheKiller/KillerFind";
        private const string ExeName        = "KillerFind.exe";
        private const string AppDisplayName = "KillerFind";

        private string? _updateTag;

        private static string CurrentVersion =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.99";

        private void ShowAboutOverlay()
        {
            AboutVersionBlock.Text = $"v{CurrentVersion}";

            var (subject, thumb) = GetSignerInfo();
            AboutPublisherBlock.Text  = subject;
            AboutThumbprintBlock.Text = thumb;
            AboutSha256Block.Text     = "computing...";
            AboutUpdateButton.Visibility = Visibility.Collapsed;

            FadeOverlayIn(AboutOverlay);

            Task.Run(() =>
            {
                var h = GetExeSha256();
                Dispatcher.BeginInvoke((Action)(() => AboutSha256Block.Text = h));
            });
            CheckForUpdateAsync(Assembly.GetExecutingAssembly().GetName().Version);
        }

        private static void FadeOverlayIn(UIElement o)
        {
            o.Visibility = Visibility.Visible;
            Anim.FadeIn(o);
        }

        private static void FadeOverlayOut(UIElement o)
        {
            var a = new DoubleAnimation(o.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(Anim.FadeMs)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            a.Completed += (_, _) => o.Visibility = Visibility.Collapsed;
            o.BeginAnimation(UIElement.OpacityProperty, a);
        }

        private void AboutOverlay_Click(object sender, MouseButtonEventArgs e) => FadeOverlayOut(AboutOverlay);
        private void AboutCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void AboutClose_Click(object sender, RoutedEventArgs e) => FadeOverlayOut(AboutOverlay);

        private void AboutVersion_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl($"https://github.com/{GitHubRepo}/releases/tag/v{CurrentVersion}");

        private void AboutLink_Navigate(object sender, RequestNavigateEventArgs e)
        {
            OpenUrl(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private void AboutUpdateButton_Click(object sender, RoutedEventArgs e) => DoSelfUpdateAsync();

        private async void DoSelfUpdateAsync()
        {
            var tag = _updateTag;
            if (string.IsNullOrEmpty(tag)) return;

            var confirm = MessageBox.Show(this,
                $"Download and install {AppDisplayName} {tag}?\n\nThe app will close and reopen automatically.",
                AppDisplayName, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            AboutUpdateButton.IsEnabled = false;
            AboutUpdateText.Text = "Downloading...";

            string? newExe = null;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppDisplayName}-UpdateCheck");

                var exeUrl  = $"https://github.com/{GitHubRepo}/releases/download/{tag}/{ExeName}";
                var sumsUrl = $"https://github.com/{GitHubRepo}/releases/download/{tag}/SHA256SUMS.txt";

                var exeBytes = await http.GetByteArrayAsync(exeUrl);
                var sumsTxt  = await http.GetStringAsync(sumsUrl);

                string? expected = null;
                foreach (var line in sumsTxt.Replace("\r", "").Split('\n'))
                {
                    if (line.TrimStart().StartsWith(ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) expected = parts[^1];
                        break;
                    }
                }
                if (string.IsNullOrEmpty(expected)) throw new Exception("checksum entry not found");

                string actual;
                using (var sha = SHA256.Create())
                    actual = BitConverter.ToString(sha.ComputeHash(exeBytes)).Replace("-", "");
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("checksum mismatch");

                newExe = Path.Combine(Path.GetTempPath(), $"{AppDisplayName}_update_{Guid.NewGuid():N}.exe");
                File.WriteAllBytes(newExe, exeBytes);
            }
            catch
            {
                AboutUpdateButton.IsEnabled = true;
                AboutUpdateText.Text = $"Update available: {tag}";
                OpenUrl($"https://github.com/{GitHubRepo}/releases/latest");
                return;
            }

            try
            {
                var curExe = Process.GetCurrentProcess().MainModule!.FileName;
                var pid    = Process.GetCurrentProcess().Id;
                var bat    = Path.Combine(Path.GetTempPath(), $"{AppDisplayName}_update_{Guid.NewGuid():N}.bat");

                File.WriteAllText(bat,
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
                    "if not errorlevel 1 ( ping -n 2 127.0.0.1 >nul & goto wait )\r\n" +
                    $"copy /y \"{newExe}\" \"{curExe}\" >nul\r\n" +
                    $"start \"\" \"{curExe}\"\r\n" +
                    $"del \"{newExe}\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n");

                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                });

                Application.Current.Shutdown();
            }
            catch
            {
                AboutUpdateButton.IsEnabled = true;
                AboutUpdateText.Text = $"Update available: {tag}";
            }
        }

        private async void CheckForUpdateAsync(Version? current)
        {
            if (current is null) return;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppDisplayName}-UpdateCheck");
                var json = await http.GetStringAsync(
                    $"https://api.github.com/repos/{GitHubRepo}/releases/latest").ConfigureAwait(false);

                var m = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success) return;
                if (!Version.TryParse(m.Groups[1].Value.TrimStart('v', 'V').Trim(), out var latest)) return;

                var cur = new Version(current.Major, current.Minor, current.Build < 0 ? 0 : current.Build);
                var lat = new Version(latest.Major, latest.Minor, latest.Build < 0 ? 0 : latest.Build);
                if (lat <= cur) return;

                await Dispatcher.BeginInvoke((Action)(() =>
                {
                    _updateTag = $"v{lat.ToString(3)}";
                    AboutUpdateText.Text = $"Update available: {_updateTag}";
                    AboutUpdateButton.Visibility = Visibility.Visible;
                }));
            }
            catch { /* offline or API error - silently ignore */ }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser available - ignore */ }
        }

        private static (string subject, string thumb) GetSignerInfo()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return ("(unavailable)", "(none)");
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                var subj = cert.GetNameInfo(X509NameType.SimpleName, false);
                return (string.IsNullOrEmpty(subj) ? cert.Subject : subj, cert.Thumbprint ?? "(none)");
            }
            catch { return ("(not signed)", "(none)"); }
        }

        private static string GetExeSha256()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "(unavailable)";
                using var sha = SHA256.Create();
                using var fs  = File.OpenRead(path);
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
            catch { return "(unavailable)"; }
        }
    }
}
