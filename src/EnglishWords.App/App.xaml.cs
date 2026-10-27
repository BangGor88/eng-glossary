using System.IO;
using System.Net.Http;
using System.Windows;
using EnglishWords.Core.Services;
using EnglishWords.Infrastructure.Ollama;
using EnglishWords.Infrastructure.Persistence;
using EnglishWords.App.ViewModels;

namespace EnglishWords.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnglishWords");
        var startupLogPath = Path.Combine(dataDirectory, "startup-error.log");
        try
        {
            Directory.CreateDirectory(dataDirectory);
            if (File.Exists(startupLogPath))
            {
                File.Delete(startupLogPath);
            }

            var repository = new SqliteVocabularyRepository(Path.Combine(dataDirectory, "lessons.db"));
            await repository.InitializeAsync();

            var httpClient = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:11434/"),
                Timeout = TimeSpan.FromMinutes(3)
            };
            var generator = new OllamaLessonGenerator(httpClient);
            var lessonService = new DailyLessonService(repository, generator, TimeProvider.System);
            var viewModel = new MainViewModel(repository, lessonService, TimeProvider.System);
            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
            await viewModel.LoadAsync();
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(startupLogPath, exception.ToString());
            MessageBox.Show($"{exception.Message}\n\nDetails saved to {startupLogPath}", "English Words", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}