# NERV — заметки для Claude

- Архитектура: `docs/architecture.md`. Полный контекст проекта (решения, окружение, этапы, что дальше) лежит в документе проекта NERV на claude.ai: `claude/context.md`.
- Ядро: `core/` (C# netstandard2.1, без Unity). Тесты: `dotnet run -c Release --project core/tests/Nerv.Core.Tests`. Должно проходить 56/56.
- Если NuGet недоступен: `dotnet restore --source /tmp/emptysrc`, затем `dotnet build --no-restore`.
- Горение: `dotnet run -c Release --project core/tools/Nerv.Core.Diag -- h2|ch4 32 10`.
- Клиент: `client/` (Unity 6000.6.9f1, URP). Химические данные копируются из `core/data` в `client/Assets/NERV/Resources/NervData/*.txt`, при правке данных обновлять обе копии.
- Язык общения и комментариев — русский.
