using System.Windows;
using System.Windows.Controls;
using RhythKit.Models;
using RhythKit.ViewModels;

namespace RhythKit.Controls;

public partial class ColorsetMakerPage : UserControl
{
    public ColorsetMakerPage()
    {
        InitializeComponent();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ColorsetMakerViewModel vm)
            return;
        if (sender is Button btn && btn.DataContext is ColorItem item)
        {
            vm.SelectedItem = item;
        }
    }
}
