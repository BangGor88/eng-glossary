using System.IO;
using System.Net.Http;
using System.Windows;
using EnglishWords.App.ViewModels;
using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using EnglishWords.Infrastructure.Dictionaries;
using EnglishWords.Infrastructure.Ollama;
using EnglishWords.Infrastructure.Persistence;

namespace EnglishWords.App;

public partial class App : Application
{
    private readonly HttpClient _ollamaHttpClient = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = TimeSpan.FromMinutes(3)
    };

    private readonly HttpClient _dictionaryHttpClient = new()
    {
        BaseAddress = new Uri("https://sv.wiktionary.org/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly HttpClient _englishDictionaryHttpClient = new()
    {
        BaseAddress = new Uri("https://en.wiktionary.org/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private MainWindow? _activeWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var selection = new LanguageSelectionWindow();
            if (selection.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            await OpenModeAsync(selection.SelectedMode);
        }
        catch (Exception exception)
        {
            ShowStartupError(exception, "EnglishWords");
            Shutdown(1);
        }
    }

    private async Task SelectLanguageAsync(MainWindow owner)
    {
        try
        {
            var selection = new LanguageSelectionWindow { Owner = owner };
            if (selection.ShowDialog() == true)
            {
                await OpenModeAsync(selection.SelectedMode);
            }
        }
        catch (Exception exception)
        {
            var modeDirectory = owner.Title switch
            {
                "Swedish Words" => "SwedishWords",
                "Mixed Words" => "MixedWords",
                _ => "EnglishWords"
            };
            ShowStartupError(exception, modeDirectory);
        }
    }

    private async Task OpenModeAsync(LanguageMode mode)
    {
        var (modeName, modeDirectory) = mode switch
        {
            LanguageMode.English => ("English Words", "EnglishWords"),
            LanguageMode.Swedish => ("Swedish Words", "SwedishWords"),
            LanguageMode.Mixed => ("Mixed Words", "MixedWords"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            modeDirectory);
        Directory.CreateDirectory(dataDirectory);
        var startupLogPath = Path.Combine(dataDirectory, "startup-error.log");
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        var repository = new SqliteVocabularyRepository(Path.Combine(dataDirectory, "lessons.db"));
        await repository.InitializeAsync();

        var englishCandidateGenerator = new OllamaLessonGenerator(_ollamaHttpClient, VocabularyLanguage.English);
        var englishGenerator = new EnglishDictionaryLessonGenerator(
            englishCandidateGenerator,
            new EnglishWiktionaryDefinitionProvider(_englishDictionaryHttpClient));
        var swedishDefinitions = new SwedishWiktionaryDefinitionProvider(_dictionaryHttpClient);
        var swedishTranslations = new EnglishWiktionaryTranslationProvider(_englishDictionaryHttpClient);
        var swedishGenerator = new SwedishDictionaryLessonGenerator(
            englishCandidateGenerator,
            swedishTranslations,
            swedishDefinitions);

        ILessonGenerator generator = mode switch
        {
            LanguageMode.English => englishGenerator,
            LanguageMode.Swedish => swedishGenerator,
            _ => new MixedLessonGenerator(englishGenerator, swedishGenerator)
        };

        var lessonService = new DailyLessonService(
            repository,
            generator,
            TimeProvider.System,
            mode == LanguageMode.English ? null : swedishDefinitions);
        MainWindow? window = null;
        var viewModel = new MainViewModel(
            repository,
            lessonService,
            TimeProvider.System,
            () =>
            {
                if (window is not null)
                {
                    _ = SelectLanguageAsync(window);
                }
            });
        window = new MainWindow(viewModel) { Title = modeName };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                Shutdown();
            }
        };

        var previousWindow = _activeWindow;
        _activeWindow = window;
        MainWindow = window;
        window.Show();
        previousWindow?.Close();
        await viewModel.LoadAsync();
    }

    private static void ShowStartupError(Exception exception, string dataDirectoryName)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            dataDirectoryName);
        Directory.CreateDirectory(dataDirectory);
        var startupLogPath = Path.Combine(dataDirectory, "startup-error.log");
        File.WriteAllText(startupLogPath, exception.ToString());
        MessageBox.Show(
            $"{exception.Message}\n\nDetails saved to {startupLogPath}",
            "English Words",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
