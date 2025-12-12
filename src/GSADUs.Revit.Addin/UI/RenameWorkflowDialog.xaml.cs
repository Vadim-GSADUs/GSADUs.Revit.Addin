using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    public partial class RenameWorkflowDialog : Window
    {
        public string? ResultName { get; private set; }

        public RenameWorkflowDialog(string? currentName)
        {
            InitializeComponent();
            NameBox.Text = currentName ?? string.Empty;
            Loaded += (_, _) =>
            {
                NameBox.CaretIndex = NameBox.Text.Length;
                NameBox.SelectAll();
                NameBox.Focus();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var text = NameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Name cannot be empty.", "Rename Workflow", MessageBoxButton.OK, MessageBoxImage.Information);
                NameBox.Focus();
                return;
            }

            ResultName = text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
