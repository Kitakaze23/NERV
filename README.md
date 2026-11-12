# NERV

Интерактивная 3D-песочница по химии: атомы из таблицы Менделеева взаимодействуют по законам химии,
реакции наглядны и эффектны. Сейчас — компьютер, архитектура заложена под VR и AR.

- **Аудитория:** от школьников до студентов; «вау-песочница» и базовые исследования.
- **Движок клиента:** Unity 6 + OpenXR (этап 3).
- **Ядро симуляции:** чистый C# (netstandard2.1, C# 9) без зависимостей от Unity.

Полная архитектура: [docs/architecture.md](docs/architecture.md).

## Статус

| Этап | Что | Статус |
|---|---|---|
| 1 | **Ядро симуляции**: модель данных, правила валентности, силовое поле UFF, команды/события, снимки, тесты | ✅ готово |
| 2 | **Реакции и названия**: каталог (YAML + reaction SMARTS), канонизатор, база веществ и ионов, ионные ассоциации | ✅ готово |
| 2.5 | **Ускорение ядра**: сетка ячеек (200 атомов ≈ 0,15 мс/тик), фоновый поток `SimulationHost` | ✅ готово |
| 3 | Unity-клиент для компьютера: рендер «стекло + неон», ввод лучом, UI | ⏳ далее |
| 4 | Эффекты, уровни качества, производительность | — |

## Структура

```
core/
  src/Nerv.Core/            ядро (одновременно UPM-пакет com.nerv.core для Unity)
    Numerics/               Vec3, детерминированный ГСЧ xoshiro256**
    Data/                   таблица Менделеева (118), параметры UFF
    Chemistry/              электронные правила, типизация, формулы, граф молекулы, SMILES/SMARTS, канонизатор
    Naming/                 база названий, таблица ионов, ионные ассоциации, распознавание
    Reactions/              каталог реакций, мини-YAML, разбор уравнений, валидатор
    Model/                  World (SoA + стабильные AtomId), связи, молекулы
    Physics/                силовое поле, термостат
    Sim/                    Simulation, команды, события, правила топологии, движок реакций, снимки
  data/                     compounds.tsv (база названий), ions.tsv, reactions.yaml (каталог реакций)
  tests/Nerv.Core.Tests/    тесты (свой мини-раннер, без внешних пакетов)
  bench/Nerv.Core.Bench/    бенчмарк: время на тик по фазам для 100/200/500 атомов
docs/
  architecture.md
```

## Сборка и тесты

Нужен .NET 8 SDK.

```bash
cd core
dotnet run --project tests/Nerv.Core.Tests            # все тесты
dotnet run --project tests/Nerv.Core.Tests Water      # фильтр по имени
dotnet run -c Release --project bench/Nerv.Core.Bench # бенчмарк
```

Тесты проверяют химию, а не конкретные траектории: вода собирается с углом ~104,5°, метан — тетраэдр,
O=O и N≡N получают кратные связи, CO₂ линейна, Na + Cl → Na⁺Cl⁻, связь H–H рвётся при нагреве,
силы совпадают с конечными разностями энергии, два прогона с одним seed совпадают побитово.
Этап 2: канонический ключ не зависит от записи и порядка атомов, вещества распознаются по названию,
искра поджигает водород (радикальная цепь), метан горит, Na + Cl₂ даёт поваренную соль,
в воде HCl диссоциирует и нейтрализуется щёлочью.

## Пример использования ядра

```csharp
using Nerv.Core;
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

var data = ChemistryData.LoadDirectory("core/data");   // названия, ионы, реакции (можно и без них)
var sim = new Simulation(new SimulationSettings { TargetTemperature = 298 }, seed: 42, data: data);

var o  = sim.Apply(new AddAtom("O", new Vec3(0, 0, 0))).Result;
var h1 = sim.Apply(new AddAtom("H", new Vec3(2.5, 0, 0))).Result;
var h2 = sim.Apply(new AddAtom("H", new Vec3(-1.25, 2.17, 0))).Result;

sim.Step(6000);                          // 3 пс (шаг 0,5 фс)

foreach (var m in sim.World.Molecules)   // → H2O «Вода»
    System.Console.WriteLine(m.Formula + " «" + m.Name + "»");

foreach (var e in sim.Events)            // BondForming, BondFormed, MoleculeIdentified «Вода»…
    System.Console.WriteLine(e);

var snapshot = new WorldSnapshot();      // для рендера: без аллокаций в кадре
sim.WriteSnapshot(snapshot);
```

В клиенте ядро запускается через `SimulationHost`: симуляция идёт в своём потоке, кадр забирает готовый снимок и события.

```csharp
var host = new SimulationHost(sim);
host.Start();                                  // фоновый поток (или host.Advance(dt) из кадра)
host.Submit(new Grab(atomId, target));         // ввод — из любого потока
host.CopySnapshot(snapshot);                   // в кадре: координаты, связи, молекулы с названиями
host.DrainEvents(events);                      // эффекты: BondFormed, ReactionFired, HeatReleased…
```

Все изменения мира — команды (`AddAtom`, `RemoveAtom`, `Grab`/`MoveGrab`/`Release`, `SetTemperature`,
`SetMedium`, `Spark`, …). Они пишутся в журнал: отсюда повторы, сохранения и будущий мультиплеер.

## Химические данные

- `core/data/compounds.tsv` — база названий (курируемое ядро): SMILES, русское и тривиальные названия, категория, опасность, факты.
- `core/data/ions.tsv` — ионы: название иона и часть названия соли («хлорид» + «натрия»).
- `core/data/reactions.yaml` — каталог реакций: шаги в reaction SMARTS с картой атомов, Ea, ΔH, условия, тексты.
  Каталог проверяется при загрузке (сохранение атомов и заряда, валентности, баланс итогового уравнения).

Чтобы добавить вещество или реакцию, достаточно отредактировать эти файлы — код менять не нужно.

## Клиент Unity (этап 3)

`client/` — проект Unity 6.6 (6000.6.9f1, URP). Ядро подключено как локальный пакет
(`client/Packages/manifest.json` → `file:../../core/src/Nerv.Core`), ничего ставить вручную не нужно.

1. Unity Hub → *Add project from disk* → папка `client`.
2. Открыть любую сцену и нажать Play — NERV запускается сам (бутстрап без настройки сцены).
3. Химические данные копируются из `core/data` автоматически; вручную — меню *NERV → Обновить химические данные*.

Управление: ПКМ / Alt+ЛКМ — вращение, СКМ / Shift+ПКМ — сдвиг, колесо — зум, 1–8 — элементы, Space — пауза.

## Единицы ядра

Å, фс, а.е.м., ккал/моль, заряд e, K. Перевод в метры сцены — на стороне клиента.
