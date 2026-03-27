# Установщик (MSI через WiX)

Сборка MSI-установщика для `Межпланетный маневр`.

## Требования

- .NET SDK
- WiX Toolset CLI (`wix`)
  - Скрипт сборки сам поставит `wix` как `dotnet` global tool, если команды `wix` нет.

## Сборка

Из папки `InterplanetaryManeuver`:

```powershell
.\installer-msi\build.ps1 -Configuration Release
```

Результат:

- `dist\installer\InterplanetaryManeuver_0.2.0_x64.msi`
