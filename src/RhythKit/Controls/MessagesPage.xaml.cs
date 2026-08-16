using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RhythKit.ViewModels;

namespace RhythKit.Controls;

public partial class MessagesPage : UserControl
{
    private MessagesViewModel? _vm;

    public MessagesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MessagesViewModel vm)
            {
                _vm = vm;
                vm.PropertyChanged += Vm_PropertyChanged;
                vm.Start();
            }
        };
        Unloaded += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
                _vm.Stop();
            }
        };
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessagesViewModel.SelectedConversation))
        {
            var hasConv = _vm?.SelectedConversation != null;
            EmptyPane.Visibility = hasConv ? Visibility.Collapsed : Visibility.Visible;
            ChatPane.Visibility = hasConv ? Visibility.Visible : Visibility.Collapsed;
            ScrollToBottom();
        }
        else if (e.PropertyName == nameof(MessagesViewModel.ScrollToBottomToken))
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (MessagesScroll.ScrollableHeight > 0)
                MessagesScroll.ScrollToEnd();
        }));
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm?.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _vm?.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
