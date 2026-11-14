# macos controls

Портативная утилита для Windows 10 и 11. Сочетание **Alt+Q** запрашивает штатное закрытие всех окон процесса активной программы. Программа может показать диалог сохранения или остаться в трее, если закрытие окон у неё настроено таким образом.

[Скачать macos controls.exe](./macos%20controls.exe?raw=true) и запустить. Через значок программы в трее можно приостановить Alt+Q или выйти из утилиты. Если Alt+Q уже переназначено в PowerToys или AutoHotkey, освободи сочетание перед запуском.

## Сборка из исходников

Из папки репозитория в PowerShell на 64-битной Windows:

```powershell
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /optimize+ /codepage:65001 /target:winexe '/out:macos controls.exe' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:AltQTray.ico /resource:AltQTray.ico,AltQTray.ico AltQTray.cs
```
