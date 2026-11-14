# NERV — заметки для Claude

- Архитектура: `docs/architecture.md`. Полный контекст проекта (решения, окружение, этапы, что дальше) лежит в документе проекта NERV на claude.ai: `claude/context.md`.
- Ядро: `core/` (C# netstandard2.1, без Unity). Тесты: `dotnet run -c Release --project core/tests/Nerv.Core.Tests`. Должно проходить 77/77.
- Если NuGet недоступен: `dotnet restore --source /tmp/emptysrc`, затем `dotnet build --no-restore`.
- Горение: `dotnet run -c Release --project core/tools/Nerv.Core.Diag -- h2|ch4 32 10`.
- Проверка компиляции клиента без Unity: `dotnet build tools/client-check -p:UnityRefs=<папка с dll>`. Нужные dll берутся с Mac пользователя через мост: `UnityEngine.*Module.dll` (включая ScreenCaptureModule) из `/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine` (доступ к папке уже выдан) и `UnityEngine.UI`, `Unity.InputSystem`, `Unity.RenderPipelines.*`, `Unity.TextMeshPro` из `client/Library/ScriptAssemblies`. Перед записью файлов клиента на Mac всегда прогонять эту проверку.
- Клиент: `client/` (Unity 6000.6.9f1, URP). Химические данные копируются из `core/data` в `client/Assets/NERV/Resources/NervData/*.txt`, при правке данных обновлять обе копии.
- Язык общения и комментариев — русский.
