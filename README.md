# English Words

A Windows desktop app that teaches six words each day with a read-the-card step, a short three-choice quiz, and achievement tracking. At startup, choose English, Swedish, or mixed English/Swedish practice. New words in mixed sets alternate between languages. Cards are written in the vocabulary word's language. One or two words return from the previous day's final set; the remaining cards contain new words. After passing a quiz, the learner can generate another distinct six-word set on the same day. Each mode has a separate local SQLite database. Ollama's `llama3.2:3b` only proposes English words. Every card's definition, part of speech, example sentence, and pronunciation comes from Wiktionary: English cards from [English Wiktionary](https://en.wiktionary.org/), and Swedish cards by translating each English word with English Wiktionary and then reading the entry on [Swedish Wiktionary](https://sv.wiktionary.org/).

## Requirements

- Windows 10 version 22H2 or newer
- .NET 10 SDK
- Ollama for Windows
- The `llama3.2:3b` model for vocabulary candidates (about 2 GB to download; memory needs vary by quantization and context size)

Install the model in PowerShell:

```powershell
ollama pull llama3.2:3b
```

Ollama must be running at its default local address, `http://localhost:11434`. The app does not download models or send lesson data to a remote service.

## Run

From the repository folder:

```powershell
dotnet restore .\EnglishWords.sln
dotnet test .\EnglishWords.sln
dotnet run --project .\src\EnglishWords.App\EnglishWords.App.csproj
```

The app prompts for a language mode at each launch. Use **Change language** in the lesson window to return to the selector and switch modes without restarting. New sets use the generator for the currently selected mode: English, Swedish, or alternating mixed words. English, Swedish, and mixed progress are stored separately at `%LOCALAPPDATA%\EnglishWords\lessons.db`, `%LOCALAPPDATA%\SwedishWords\lessons.db`, and `%LOCALAPPDATA%\MixedWords\lessons.db`. Tap each of the six word cards once to reveal its definition, example, and pronunciation hint. Swedish words are translated with [English Wiktionary](https://en.wiktionary.org/), and their definitions, examples, and pronunciation come from [Swedish Wiktionary](https://sv.wiktionary.org/); English definitions, examples, and IPA pronunciation come from English Wiktionary. The language model does not write card content, so all modes require internet access to those sites. Each card links to its source entry and identifies the CC BY-SA 4.0 license. A word is only added to a lesson if a definition and example sentence are found (and, for Swedish, a translation); sets can take up to a minute or two because dictionary requests are paced to respect Wiktionary's rate limits. After all six have been read, the quiz asks six questions with three choices each, covering meaning and example only. At least three correct answers finish the set; otherwise, the child can retry. Each correct answer adds to that word's consecutive-correct streak and lifetime correct count; a wrong answer resets the streak. Five correct answers in a row mark a word as mastered. The Scorecard button shows learned words and how often each has been answered correctly. Existing five-word English lessons are upgraded by adding a sixth card.

## Publish a Windows EXE

From the repository folder, publish a self-contained single-file Windows x64 app:

```powershell
dotnet publish .\src\EnglishWords.App\EnglishWords.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```

The executable is `publish\win-x64\EnglishWords.App.exe`. The .NET runtime and the app's SQLite native library are bundled, so the target PC does not need the .NET runtime installed. Windows may extract the bundled native library to a temporary directory when the app starts.

Ollama and the model are not bundled. On each target PC, install Ollama separately, run `ollama pull llama3.2:3b`, and leave Ollama running at `http://localhost:11434`. All modes use this model only to propose words. The app requires Windows 10 version 22H2 or newer. Each mode stores its lessons and scorecard in its own local database.

## Troubleshooting

- **Ollama unavailable:** Start the Ollama application and retry.
- **Model missing:** Run `ollama pull llama3.2:3b` in PowerShell, then retry.
- **Generation failed:** Retry after Ollama finishes loading the model. Incomplete responses are rejected rather than saved.