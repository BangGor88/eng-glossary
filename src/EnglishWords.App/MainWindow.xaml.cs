using System.Windows;
using EnglishWords.App.ViewModels;

namespace EnglishWords.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}