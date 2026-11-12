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
| 2 | Каталог реакций (YAML + reaction SMARTS), канонизатор, база названий | ⏳ далее |
| 3 | Unity-клиент для компьютера: рендер «стекло + неон», ввод лучом, UI | — |
| 4 | Эффекты, уровни качества, производительность | — |

## Структура

```
core/
  src/Nerv.Core/            ядро (одновременно UPM-пакет com.nerv.core для Unity)
    Numerics/               Vec3, детерминированный ГСЧ xoshiro256**
    Data/                   таблица Менделеева (118), параметры UFF
    Chemistry/              электронные правила, типизация атомов, формулы (Хилл)
    Model/                  World (SoA + стабильные AtomId), связи, молекулы
    Physics/                силовое поле, термостат
    Sim/                    Simulation, команды, события, правила топологии, снимки
  tests/Nerv.Core.Tests/    тесты (свой мини-раннер, без внешних пакетов)
docs/
  architecture.md
```

## Сборка и тесты

Нужен .NET 8 SDK.

```bash
cd core
dotnet run --project tests/Nerv.Core.Tests            # все тесты
dotnet run --project tests/Nerv.Core.Tests Water      # фильтр по имени
```

Тесты проверяют химию, а не конкретные траектории: вода собирается с углом ~104,5°, метан — тетраэдр,
O=O и N≡N получают кратные связи, CO₂ линейна, Na + Cl → Na⁺Cl⁻, связь H–H рвётся при нагреве,
силы совпадают с конечными разностями энергии, два прогона с одним seed совпадают побитово.

## Пример использования ядра

```csharp
using Nerv.Core.Numerics;
using Nerv.Core.Sim;

var sim = new Simulation(new SimulationSettings { TargetTemperature = 298 }, seed: 42);

var o  = sim.Apply(new AddAtom("O", new Vec3(0, 0, 0))).Result;
var h1 = sim.Apply(new AddAtom("H", new Vec3(2.5, 0, 0))).Result;
var h2 = sim.Apply(new AddAtom("H", new Vec3(-1.25, 2.17, 0))).Result;

sim.Step(6000);                          // 3 пс (шаг 0,5 фс)

foreach (var m in sim.World.Molecules)   // → H2O
    System.Console.WriteLine(m.Formula);

foreach (var e in sim.Events)            // BondForming, BondFormed, TopologyChanged…
    System.Console.WriteLine(e);

var snapshot = new WorldSnapshot();      // для рендера: без аллокаций в кадре
sim.WriteSnapshot(snapshot);
```

Все изменения мира — команды (`AddAtom`, `RemoveAtom`, `Grab`/`MoveGrab`/`Release`, `SetTemperature`,
`SetMedium`, `Spark`, …). Они пишутся в журнал: отсюда повторы, сохранения и будущий мультиплеер.

## Подключение к Unity (этап 3)

Ядро уже оформлено как локальный UPM-пакет: Package Manager → *Add package from disk…* →
`core/src/Nerv.Core/package.json`. Сборочные артефакты .NET лежат в `core/artifacts/` и в пакет не попадают.

## Единицы ядра

Å, фс, а.е.м., ккал/моль, заряд e, K. Перевод в метры сцены — на стороне клиента.
