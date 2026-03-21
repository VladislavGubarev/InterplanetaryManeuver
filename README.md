# Interplanetary Maneuver

Настольное Windows-приложение для моделирования межпланетного гравитационного маневра у Юпитера с визуализацией траекторий, скоростей и поиском параметров пролета.

## Что умеет

- моделирует систему тел методом `RK-45` (`Dormand-Prince`)
- использует реальные эфемериды через `JPL Horizons`
- считает метрики гравиманевра у Юпитера
- подбирает параметры пролета перебором
- показывает траектории, скорости и компоненты `Vx`, `Vy`, `Vz`
- содержит песочницу с пользовательскими телами
- содержит анимацию движения тел

## Стек

- `.NET 8`
- `WPF`
- `WPF UI`
- C#

## Структура проекта

- `InterplanetaryManeuver.App` — WPF-приложение и интерфейс
- `PhysicsSim.Core` — физическая модель, интегратор и расчет метрик
- `docs` — документация по математической модели и реализации
- `installer` — сборка `exe`-установщика
- `installer-msi` — сборка `msi`-установщика
- `assets/branding` — иконки и графика установщика

## Запуск из исходников

Требуется:

- `Visual Studio 2022` или `VS Code` + `.NET 8 SDK`
- Windows

Сборка решения:

```powershell
dotnet build .\InterplanetaryManeuver.sln
```

Запуск приложения:

```powershell
dotnet run --project .\InterplanetaryManeuver.App\InterplanetaryManeuver.App.csproj
```

## Сборка установщиков

`EXE`-установщик:

```powershell
.\installer\build.ps1 -Configuration Release
```

`MSI`-установщик:

```powershell
.\installer-msi\build.ps1 -Configuration Release
```

## Документация

Основные материалы находятся здесь:

- `docs/interplanetary_maneuver_documentation.md`
- `docs/interplanetary_maneuver_documentation.html`
- `docs/interplanetary_maneuver_documentation.pdf`

В документации описаны:

- используемые формулы
- постановка задачи
- метод интегрирования
- расчет прироста скорости
- структура реализации в коде

## Замечания

- В репозиторий не включаются артефакты сборки `bin/`, `obj/`, `dist/`, `.vs/`
- Готовые `exe` и `msi` лучше публиковать через `GitHub Releases`, а не хранить в git

