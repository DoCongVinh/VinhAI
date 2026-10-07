using System.Windows;
using System.Windows.Input;

namespace AIOrchestrator.Views
{
    public partial class RenameConversationWindow : Window
    {
        public string ConversationTitle => TxtTitle.Text.Trim();

        public RenameConversationWindow(string title)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtTitle.SelectAll();
            Loaded += (_, _) => TxtTitle.Focus();
        }

        private void TxtTitle_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SaveTitle();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveTitle();

        private void SaveTitle()
        {
            if (string.IsNullOrWhiteSpace(ConversationTitle))
            {
                MessageBox.Show("Vui lòng nhập tên cuộc trò chuyện.", "Thiếu tên",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
