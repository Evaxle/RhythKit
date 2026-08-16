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
        if (DataContext is ColorsetMakerViewModel vm &&
            sender is Button { DataContext: ColorItem item })
        {
            vm.SelectedItem = item;
        }
    }
}
