using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using EnglishWords.Infrastructure.Ollama;

namespace EnglishWords.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int PassingScore = 3;
    private readonly IVocabularyRepository _repository;
    private readonly DailyLessonService _lessonService;
    private readonly Action _requestLanguageSelection;
    private DailyLesson? _lesson;
    private int _questionIndex;
    private int _quizScore;
    private string _pageBeforeScorecard = "Cards";
    private bool _isBusy;
    private bool _isAnswerSubmitted;
    private string _page = "Cards";
    private string _statusMessage = "Connecting to Ollama...";
    private string _errorMessage = string.Empty;
    private string _answerFeedback = string.Empty;

    public MainViewModel(
        IVocabularyRepository repository,
        DailyLessonService lessonService,
        TimeProvider timeProvider,
        Action requestLanguageSelection)
    {
        _repository = repository;
        _lessonService = lessonService;
        _requestLanguageSelection = requestLanguageSelection;
        RetryCommand = new AsyncCommand(_ => LoadAsync(), _ => !IsBusy);
        FlipCardCommand = new AsyncCommand(FlipCardAsync, _ => !IsBusy && !IsPracticeComplete);
        SelectAnswerCommand = new AsyncCommand(SelectAnswerAsync, parameter => !IsBusy && !IsAnswerSubmitted && parameter is QuizOption);
        ContinueCommand = new RelayCommand(_ => ContinueQuiz(), _ => !IsBusy && IsAnswerSubmitted && !IsLastQuestion);
        RetryQuizCommand = new AsyncCommand(_ => StartQuizAsync(), _ => !IsBusy && _lesson is { QuizComplete: true, QuizPassed: false });
        FinishCommand = new RelayCommand(_ => Page = "Cards", _ => !IsBusy && IsPracticeComplete);
        OpenScorecardCommand = new AsyncCommand(_ => OpenScorecardAsync(), _ => !IsBusy);
        ReturnFromScorecardCommand = new RelayCommand(_ => Page = _pageBeforeScorecard, _ => !IsBusy);
        NewSetCommand = new AsyncCommand(_ => GenerateNewSetAsync(), _ => !IsBusy && IsPracticeComplete);
        ChangeLanguageCommand = new RelayCommand(_ => _requestLanguageSelection());
    }

    public ObservableCollection<WordCardViewModel> LessonWords { get; } = [];
    public ObservableCollection<QuizQuestion> Questions { get; } = [];
    public ObservableCollection<ScorecardWord> ScorecardWords { get; } = [];
    public ICommand RetryCommand { get; }
    public ICommand FlipCardCommand { get; }
    public ICommand SelectAnswerCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand RetryQuizCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand OpenScorecardCommand { get; }
    public ICommand ReturnFromScorecardCommand { get; }
    public ICommand NewSetCommand { get; }
    public ICommand ChangeLanguageCommand { get; }
    public string Page { get => _page; private set { if (SetProperty(ref _page, value)) { OnPropertyChanged(nameof(IsCardsPage)); OnPropertyChanged(nameof(IsQuizPage)); OnPropertyChanged(nameof(IsResultsPage)); OnPropertyChanged(nameof(IsScorecardPage)); NotifyCommands(); } } }
    public bool IsCardsPage => Page == "Cards";
    public bool IsQuizPage => Page == "Quiz";
    public bool IsResultsPage => Page == "Results";
    public bool IsScorecardPage => Page == "Scorecard";
    public int ReadCardCount => LessonWords.Count(card => card.IsRead);
    public int QuizScore => _lesson?.QuizScore ?? _quizScore;
    public int BestScore => _lesson?.BestScore ?? 0;
    public int QuizAttempts => _lesson?.QuizAttempts ?? 0;
    public int QuestionNumber => Math.Min(_questionIndex + 1, DailyLessonService.DailyWordCount);
    public int QuestionCount => DailyLessonService.DailyWordCount;
    public int MasteredWordCount => ScorecardWords.Count(word => word.IsMastered);
    public int TotalCorrectAnswers => ScorecardWords.Sum(word => word.TimesCorrect);
    public bool HasScorecardWords => ScorecardWords.Count > 0;
    public bool HasNoScorecardWords => !HasScorecardWords;
    public QuizQuestion? CurrentQuestion => _questionIndex < Questions.Count ? Questions[_questionIndex] : null;
    public string DetailLabel => CurrentQuestion?.Detail switch { QuizDetail.Meaning => "MEANING", QuizDetail.Example => "EXAMPLE", _ => "WORD MATCH" };
    public string QuestionPrompt => CurrentQuestion?.Detail switch { QuizDetail.Meaning => "Choose the meaning of this word", QuizDetail.Example => "Choose the example that uses this word", _ => string.Empty };
    public string AnswerFeedback { get => _answerFeedback; private set => SetProperty(ref _answerFeedback, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); } }
    public bool IsAnswerSubmitted { get => _isAnswerSubmitted; private set { if (SetProperty(ref _isAnswerSubmitted, value)) NotifyCommands(); } }
    public bool IsPracticeComplete => _lesson is { QuizComplete: true, QuizPassed: true };
    public bool NeedsRetry => _lesson is { QuizComplete: true, QuizPassed: false };
    public bool IsLastQuestion => _questionIndex >= Questions.Count - 1;
    public string AchievementMessage => IsPracticeComplete
        ? $"Six cards read · {BestScore} of 6 correct · {LessonWords.Count(card => card.Word.IsMastered)} words mastered"
        : _lesson?.QuizComplete == true
            ? $"{BestScore} of 6 best score · Get 3 right to finish"
            : $"{ReadCardCount} of 6 cards read · Best quiz score {BestScore} of 6";

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Preparing today's six word cards...";
        try
        {
            _lesson = await _lessonService.GetTodayAsync(generationProgress: CreateGenerationProgress());
            LessonWords.Clear();
            foreach (var word in _lesson.Words) LessonWords.Add(new WordCardViewModel(word));
            OnPropertyChanged(nameof(ReadCardCount));
            OnPropertyChanged(nameof(QuizScore));
            OnPropertyChanged(nameof(BestScore));
            OnPropertyChanged(nameof(QuizAttempts));
            OnPropertyChanged(nameof(IsPracticeComplete));
            OnPropertyChanged(nameof(NeedsRetry));
            OnPropertyChanged(nameof(AchievementMessage));

            if (_lesson.QuizComplete)
            {
                Page = "Results";
                StatusMessage = _lesson.QuizPassed ? "Today's practice is complete." : "Try the quiz again to finish today's practice.";
            }
            else if (ReadCardCount == DailyLessonService.DailyWordCount)
            {
                await StartQuizAsync();
            }
            else
            {
                Page = "Cards";
                StatusMessage = "Tap each card to turn it over and read the details.";
            }
        }
        catch (OllamaUnavailableException exception) { SetError(exception.Message); }
        catch (OllamaModelMissingException exception) { SetError(exception.Message); }
        catch (OllamaGenerationException exception) { SetError(exception.Message); }
        catch (InvalidDataException exception) { SetError($"The lesson or quiz couldn't be prepared: {exception.Message}"); }
        catch (Exception exception) { SetError($"Today's practice couldn't be loaded: {exception.Message}"); }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(AchievementMessage));
            NotifyCommands();
        }
    }

    private async Task FlipCardAsync(object? parameter)
    {
        if (parameter is not WordCardViewModel card || _lesson is null) return;
        card.IsFlipped = !card.IsFlipped;
        if (card.IsRead) return;
        try
        {
            await _repository.MarkWordReadAsync(_lesson.Date, _lesson.SetNumber, card.Word.Id);
            card.MarkRead();
            OnPropertyChanged(nameof(ReadCardCount));
            OnPropertyChanged(nameof(AchievementMessage));
            if (ReadCardCount == DailyLessonService.DailyWordCount) await StartQuizAsync();
        }
        catch (Exception exception) { SetError($"This card's read progress couldn't be saved: {exception.Message}"); }
    }

    private async Task StartQuizAsync()
    {
        if (_lesson is null) return;
        var questions = QuizFactory.Create(_lesson);
        await _repository.StartQuizAttemptAsync(_lesson.Date, _lesson.SetNumber);
        _lesson = _lesson with { QuizScore = 0, QuizAttempts = _lesson.QuizAttempts + 1, QuizComplete = false, QuizPassed = false };
        Questions.Clear();
        foreach (var question in questions) Questions.Add(question);
        _questionIndex = 0;
        _quizScore = 0;
        IsAnswerSubmitted = false;
        AnswerFeedback = string.Empty;
        Page = "Quiz";
        StatusMessage = $"Quiz attempt {_lesson.QuizAttempts}. Match each word to one of three choices.";
        NotifyQuestionProperties();
        OnPropertyChanged(nameof(QuizScore));
        OnPropertyChanged(nameof(BestScore));
        OnPropertyChanged(nameof(QuizAttempts));
        OnPropertyChanged(nameof(IsPracticeComplete));
        OnPropertyChanged(nameof(NeedsRetry));
        OnPropertyChanged(nameof(AchievementMessage));
    }

    private async Task SelectAnswerAsync(object? parameter)
    {
        if (parameter is not QuizOption option || CurrentQuestion is not { } question || _lesson is null) return;
        var card = LessonWords.First(item => item.Word.Id == question.WordId);
        var mastery = MasteryTracker.RecordAnswer(card.Word.CorrectStreak, option.IsCorrect);
        var correctStreak = mastery.CorrectStreak;
        var mastered = card.Word.IsMastered || mastery.IsMastered;
        var timesCorrect = card.Word.TimesCorrect + (option.IsCorrect ? 1 : 0);
        _quizScore += option.IsCorrect ? 1 : 0;
        var lastQuestion = IsLastQuestion;
        var passed = lastQuestion && _quizScore >= PassingScore;
        try
        {
            await _repository.SaveQuizAnswerAsync(
                _lesson.Date,
                _lesson.SetNumber,
                card.Word.Id,
                correctStreak,
                mastered,
                timesCorrect,
                _quizScore,
                lastQuestion,
                passed);
        }
        catch (Exception exception)
        {
            _quizScore -= option.IsCorrect ? 1 : 0;
            SetError($"Your answer couldn't be saved: {exception.Message}");
            return;
        }

        card.UpdateMastery(correctStreak, mastered, timesCorrect);
        _lesson = _lesson with { QuizScore = _quizScore, BestScore = Math.Max(_lesson.BestScore, _quizScore), QuizComplete = lastQuestion, QuizPassed = passed };
        question = question with
        {
            Options = question.Options.Select(choice => choice with { IsSelected = choice.Text == option.Text }).ToArray()
        };
        Questions[_questionIndex] = question;
        OnPropertyChanged(nameof(CurrentQuestion));
        var answer = question.Options.Single(choice => choice.IsCorrect).Text;
        AnswerFeedback = option.IsCorrect ? "That's right!" : $"Not quite. The answer is: {answer}";
        IsAnswerSubmitted = true;
        StatusMessage = $"Question {QuestionNumber} of 6 · {_quizScore} correct";
        OnPropertyChanged(nameof(QuizScore));
        OnPropertyChanged(nameof(BestScore));
        OnPropertyChanged(nameof(IsPracticeComplete));
        OnPropertyChanged(nameof(NeedsRetry));
        OnPropertyChanged(nameof(AchievementMessage));
        if (lastQuestion)
        {
            Page = "Results";
            StatusMessage = passed ? "Practice complete. Great learning today!" : "Get at least 3 questions right to finish practice.";
        }
    }

    private void ContinueQuiz()
    {
        if (!IsAnswerSubmitted || IsLastQuestion) return;
        _questionIndex++;
        IsAnswerSubmitted = false;
        AnswerFeedback = string.Empty;
        NotifyQuestionProperties();
    }

    private async Task OpenScorecardAsync()
    {
        _pageBeforeScorecard = Page;
        var entries = await _repository.GetScorecardAsync();
        ScorecardWords.Clear();
        foreach (var entry in entries) ScorecardWords.Add(entry);
        OnPropertyChanged(nameof(MasteredWordCount));
        OnPropertyChanged(nameof(TotalCorrectAnswers));
        OnPropertyChanged(nameof(HasScorecardWords));
        OnPropertyChanged(nameof(HasNoScorecardWords));
        Page = "Scorecard";
    }

    private async Task GenerateNewSetAsync()
    {
        if (_lesson is null || !IsPracticeComplete) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Checking Ollama and preparing your next word set...";
        try
        {
            _lesson = await _lessonService.GenerateNewSetAsync(_lesson, generationProgress: CreateGenerationProgress());
            LessonWords.Clear();
            foreach (var word in _lesson.Words) LessonWords.Add(new WordCardViewModel(word));
            Questions.Clear();
            _questionIndex = 0;
            _quizScore = 0;
            Page = "Cards";
            StatusMessage = $"Set {_lesson.SetNumber} is ready. Tap each card to read it.";
            OnPropertyChanged(nameof(ReadCardCount));
            OnPropertyChanged(nameof(QuizScore));
            OnPropertyChanged(nameof(BestScore));
            OnPropertyChanged(nameof(QuizAttempts));
            OnPropertyChanged(nameof(AchievementMessage));
        }
        catch (Exception exception)
        {
            SetError($"A new set couldn't be generated: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyQuestionProperties()
    {
        OnPropertyChanged(nameof(CurrentQuestion));
        OnPropertyChanged(nameof(QuestionNumber));
        OnPropertyChanged(nameof(DetailLabel));
        OnPropertyChanged(nameof(QuestionPrompt));
    }

    private IProgress<string> CreateGenerationProgress() =>
        new Progress<string>(message => StatusMessage = message);

    private void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = "Progress is saved locally. Retry when ready.";
    }

    private void NotifyCommands()
    {
        (RetryCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (FlipCardCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (SelectAnswerCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (ContinueCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RetryQuizCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (FinishCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenScorecardCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (NewSetCommand as AsyncCommand)?.NotifyCanExecuteChanged();
        (ChangeLanguageCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

public sealed class WordCardViewModel(VocabularyWord word) : ObservableObject
{
    private bool _isFlipped;
    private VocabularyWord _word = word;
    public VocabularyWord Word
    {
        get => _word;
        private set
        {
            if (SetProperty(ref _word, value))
            {
                OnPropertyChanged(nameof(CardStatus));
                OnPropertyChanged(nameof(MasteryLabel));
            }
        }
    }
    public bool IsRead => Word.IsRead;
    public bool IsFlipped { get => _isFlipped; set => SetProperty(ref _isFlipped, value); }
    public string CardStatus => Word.IsMastered ? "MASTERED" : Word.IsReviewWord ? "REVISIT" : "NEW TODAY";
    public string LanguageLabel => Word.Language == VocabularyLanguage.Swedish ? "SWEDISH" : "ENGLISH";
    public string ReadLabel => IsRead ? "READ ONCE · TAP TO REVIEW" : "TAP TO TURN OVER";
    public string MasteryLabel => $"{Word.CorrectStreak} of 5 correct in a row";
    public bool HasDictionarySource => Uri.TryCreate(Word.DefinitionSource, UriKind.Absolute, out _);
    public Uri? DictionarySourceUri => HasDictionarySource ? new Uri(Word.DefinitionSource) : null;
    public void MarkRead() { if (Word.IsRead) return; Word = Word with { IsRead = true }; OnPropertyChanged(nameof(IsRead)); }
    public void UpdateMastery(int streak, bool mastered, int timesCorrect) =>
        Word = Word with { CorrectStreak = streak, IsMastered = mastered, TimesCorrect = timesCorrect };
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}