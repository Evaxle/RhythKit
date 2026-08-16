using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RhythKit.Models;

namespace RhythKit.Windows;

public partial class ContactPickerWindow : Window
{
    private readonly bool _multiSelect;

    public List<int> SelectedPlayerIds { get; private set; } = new();
    public string GroupName { get; private set; } = "";

    public ContactPickerWindow(string title, string subtitle, bool multiSelect, bool promptName)
    {
        InitializeComponent();
        _multiSelect = multiSelect;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        GroupNameBox.Visibility = promptName ? Visibility.Visible : Visibility.Collapsed;
        if (promptName)
            GroupNameBox.Text = title == "New Group Chat" ? "Group Chat" : title;
        ConfirmBtn.Content = multiSelect ? "Create" : "Add";
        ContactsList.SelectionChanged += ContactsList_SelectionChanged;
    }

    public void SetContacts(IEnumerable<ChatUser> contacts)
    {
        ContactsList.ItemsSource = contacts.ToList();
        if (_multiSelect)
        {
            ContactsList.SelectionMode = SelectionMode.Multiple;
            ConfirmBtn.IsEnabled = false;
        }
        EmptyText.Visibility = ContactsList.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ContactsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_multiSelect)
        {
            ConfirmBtn.IsEnabled = ContactsList.SelectedItems.Count > 0;
        }
        else
        {
            ConfirmBtn.IsEnabled = ContactsList.SelectedItem != null;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_multiSelect)
            SelectedPlayerIds = ContactsList.SelectedItems.Cast<ChatUser>().Select(u => u.PlayerId).ToList();
        else if (ContactsList.SelectedItem is ChatUser user)
            SelectedPlayerIds = new List<int> { user.PlayerId };

        GroupName = GroupNameBox.Text.Trim();
        DialogResult = true;
    }
}
