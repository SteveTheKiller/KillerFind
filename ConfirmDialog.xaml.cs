using System.Windows;
using System.Windows.Input;

namespace KillerFind
{
    // Grunge-themed confirm dialog (KillerScan's ConfirmDialog, generalized): a message,
    // optional bullet lines, and up to two optional checkboxes. Used by the portable
    // Install prompt and the smart-Esc quit prompt.
    public partial class ConfirmDialog : Window
    {
        public bool Confirmed     { get; private set; }
        public bool Check1Checked => Check1.IsChecked == true;
        public bool Check2Checked => Check2.IsChecked == true;

        public ConfirmDialog(string message, string? bullets, string okText,
                             string? check1Label = null, bool check1Initial = false,
                             string? check2Label = null, bool check2Initial = false)
        {
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            SourceInitialized += (_, _) => MainWindow.ApplyThemeBorder(this);

            MsgText.Text  = message;
            OkBtn.Content = okText;

            if (!string.IsNullOrEmpty(bullets))
            {
                BulletText.Text       = bullets;
                BulletText.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(check1Label))
            {
                Check1.Content    = check1Label;
                Check1.IsChecked  = check1Initial;
                Check1.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(check2Label))
            {
                Check2.Content    = check2Label;
                Check2.IsChecked  = check2Initial;
                Check2.Visibility = Visibility.Visible;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();
    }
}
