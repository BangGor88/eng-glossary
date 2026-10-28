namespace EnglishWords.Core.Abstractions;

public interface ISwedishDefinitionProvider
{
    Task<IReadOnlyDictionary<string, DictionaryDefinition>> GetDefinitionsAsync(
        IReadOnlyCollection<string> words,
        CancellationToken cancellationToken = default);

    Task<DictionaryDefinition?> GetDefinitionAsync(
        string word,
        CancellationToken cancellationToken = default);
}

public sealed record DictionaryDefinition(
    string PartOfSpeech,
    string Definition,
    string SourceUrl,
    string Example = "",
    string Pronunciation = "");

public interface IEnglishDefinitionProvider
{
    Task<DictionaryDefinition?> GetDefinitionAsync(
        string word,
        CancellationToken cancellationToken = default);
}

public interface ISwedishTranslationProvider
{
    Task<string?> GetSwedishTranslationAsync(
        string englishWord,
        CancellationToken cancellationToken = default);
}
