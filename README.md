# English Words

A Windows desktop app that teaches six English words each day with a read-the-card step, a short three-choice quiz, and achievement tracking. One or two words return from the previous day's final set; the remaining cards contain new words. After passing a quiz, the learner can generate another distinct six-word set on the same day. Completed sets and progress stay in a local SQLite database. Ollama generates words with `llama3.2:3b`.

## Requirements

- Windows 10 version 22H2 or newer
- .NET 10 SDK
- Ollama for Windows
- The `llama3.2:3b` model (about 2 GB to download; memory needs vary by quantization and context size)

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

The app creates its database at `%LOCALAPPDATA%\EnglishWords\lessons.db`. Tap each of the six word cards once to reveal its definition, example, and pronunciation hint. After all six have been read, the quiz asks six questions with three choices each, covering meaning and example only. At least three correct answers finish the set; otherwise, the child can retry. Each correct answer adds to that word's consecutive-correct streak and lifetime correct count; a wrong answer resets the streak. Five correct answers in a row mark a word as mastered. The Scorecard button shows learned words and how often each has been answered correctly. Existing five-word lessons are upgraded by adding a sixth card.

## Publish a Windows EXE

From the repository folder, publish a self-contained single-file Windows x64 app:

```powershell
dotnet publish .\src\EnglishWords.App\EnglishWords.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```

The executable is `publish\win-x64\EnglishWords.App.exe`. The .NET runtime and the app's SQLite native library are bundled, so the target PC does not need the .NET runtime installed. Windows may extract the bundled native library to a temporary directory when the app starts.

Ollama and `llama3.2:3b` are not bundled. On each target PC, install Ollama separately, run `ollama pull llama3.2:3b`, and leave Ollama running at `http://localhost:11434`. The app requires Windows 10 version 22H2 or newer. Its local lessons and scorecard are stored separately at `%LOCALAPPDATA%\EnglishWords\lessons.db`.

## Troubleshooting

- **Ollama unavailable:** Start the Ollama application and retry.
- **Model missing:** Run `ollama pull llama3.2:3b` in PowerShell, then retry.
- **Generation failed:** Retry after Ollama finishes loading the model. Incomplete responses are rejected rather than saved.