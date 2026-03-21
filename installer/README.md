# Установщик (Inno Setup)

Этот проект использует Inno Setup для создания установщика `.exe`.

## Требования

- Inno Setup 6 (в системе должен быть доступен `ISCC.exe`)
- .NET SDK (для `dotnet publish`)

## Сборка

Из папки `InterplanetaryManeuver`:

```powershell
.\installer\build.ps1 -Configuration Release
```

Результат:

- `dist\publish` содержит опубликованные файлы приложения
- `dist\installer` содержит готовый установщик

## Брендинг

Скрипт `installer\build.ps1` автоматически генерирует:

- `assets\branding\setup.ico` (иконка установщика и ярлыков)
- `assets\branding\wizard.bmp` и `assets\branding\wizard_small.bmp` (картинки мастера установки)
