namespace EnglishWords.Core.Models;

public sealed record VocabularyWord(
    Guid Id,
    int Order,
    string Word,
    string PartOfSpeech,
    string Definition,
    string Example,
    string Pronunciation,
    bool IsReviewWord,
    bool IsRead,
    int CorrectStreak,
    bool IsMastered,
    int TimesCorrect = 0,
    VocabularyLanguage Language = VocabularyLanguage.English,
    string DefinitionSource = "");

public sealed record GeneratedWord(
    string Word,
    string PartOfSpeech,
    string Definition,
    string Example,
    string Pronunciation,
    VocabularyLanguage Language = VocabularyLanguage.English,
    string DefinitionSource = "");