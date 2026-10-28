using System.Windows;
using EnglishWords.Core.Models;

namespace EnglishWords.App;

public partial class LanguageSelectionWindow : Window
{
    public LanguageMode SelectedMode { get; private set; }

    public LanguageSelectionWindow() => InitializeComponent();

    private void EnglishButton_Click(object sender, RoutedEventArgs e) => SelectMode(LanguageMode.English);

    private void SwedishButton_Click(object sender, RoutedEventArgs e) => SelectMode(LanguageMode.Swedish);

    private void MixedButton_Click(object sender, RoutedEventArgs e) => SelectMode(LanguageMode.Mixed);

    private void SelectMode(LanguageMode mode)
    {
        SelectedMode = mode;
        DialogResult = true;
    }
}
