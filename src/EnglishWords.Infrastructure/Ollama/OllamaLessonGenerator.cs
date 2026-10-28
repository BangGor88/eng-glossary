using System.Net.Http.Headers;
using System.Text.Json;
using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;

namespace EnglishWords.Infrastructure.Ollama;

public sealed class OllamaLessonGenerator(HttpClient httpClient) : ILessonGenerator
{
    public const string DefaultModel = "llama3.2:3b";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
        DateOnly date,
        int count,
        IReadOnlyCollection<string> avoidWords,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        await EnsureModelAvailableAsync(cancellationToken);
        progress?.Report($"Ollama is ready and {DefaultModel} is available. Generating {count} words...");

        var request = new
        {
            model = DefaultModel,
            stream = false,
            format = new
            {
                type = "object",
                properties = new
                {
                    words = new
                    {
                        type = "array",
                        minItems = count,
                        maxItems = count,
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                word = new { type = "string" },
                                partOfSpeech = new { type = "string" },
                                definition = new { type = "string" },
                                example = new { type = "string" },
                                pronunciation = new { type = "string" }
                            },
                            required = new[] { "word", "partOfSpeech", "definition", "example", "pronunciation" },
                            additionalProperties = false
                        }
                    }
                },
                required = new[] { "words" },
                additionalProperties = false
            },
            messages = new object[]
            {
                new { role = "system", content = "You teach practical English vocabulary to a child. Return the requested number of distinct, useful English words at an age-appropriate level. Make every definition and example distinct from the others so each quiz question has three clear choices. Use clear concise definitions, natural example sentences, common part-of-speech labels, and simple pronunciation hints in respelled English. Return only data matching the requested JSON schema." },
                new { role = "user", content = $"Create {count} new vocabulary words for {date:yyyy-MM-dd}. Avoid obscure words, avoid repeating a word within the lesson, and do not use any of these words: {string.Join(", ", avoidWords)}." }
            }
        };

        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync("api/chat", content, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException("Ollama could not be reached at http://localhost:11434. Start Ollama and retry.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException("Ollama did not respond in time. Retry when the model is ready.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new OllamaGenerationException($"Ollama returned {(int)response.StatusCode}: {details}");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<ChatResponse>(stream, JsonOptions, cancellationToken);
                var lesson = JsonSerializer.Deserialize<LessonResponse>(payload?.Message?.Content ?? string.Empty, JsonOptions);
                return lesson?.Words ?? throw new OllamaGenerationException("Ollama returned an empty lesson. Retry generation.");
            }
            catch (JsonException exception)
            {
                throw new OllamaGenerationException("Ollama returned lesson data that could not be read. Retry generation.", exception);
            }
        }
    }

    private async Task EnsureModelAvailableAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync("api/tags", cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException("Ollama could not be reached at http://localhost:11434. Start Ollama and retry.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException("Ollama did not respond in time. Retry when the model is ready.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new OllamaUnavailableException($"Ollama returned {(int)response.StatusCode} while checking installed models. Start Ollama and retry.");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var available = document.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(model => model.GetProperty("name").GetString())
                .Any(name => string.Equals(name, DefaultModel, StringComparison.OrdinalIgnoreCase));

            if (!available)
            {
                throw new OllamaModelMissingException($"The {DefaultModel} model is not installed. Run `ollama pull {DefaultModel}` in PowerShell, then retry.");
            }
        }
    }

    private sealed record ChatResponse(ChatMessage? Message);
    private sealed record ChatMessage(string? Content);
    private sealed record LessonResponse(IReadOnlyList<GeneratedWord>? Words);
}

public sealed class OllamaUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class OllamaModelMissingException(string message) : Exception(message);

public sealed class OllamaGenerationException(string message, Exception? innerException = null) : Exception(message, innerException);