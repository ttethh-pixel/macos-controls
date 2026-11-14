# macos controls

[English](#english)

Портативная утилита для Windows 10 и 11. Сочетание **Alt+Q** запрашивает штатное закрытие всех окон процесса активной программы. Программа может показать диалог сохранения или остаться в трее, если закрытие окон у неё настроено таким образом.

[Скачать macos controls.exe](./macos%20controls.exe?raw=true) и запустить. Через значок программы в трее можно приостановить Alt+Q или выйти из утилиты. Если Alt+Q уже переназначено в PowerToys или AutoHotkey, освободи сочетание перед запуском.

## Жесты колёсиком

Зажми колёсико, проведи мышью в нужную сторону и отпусти. Порог жеста — 56 пикселей.

| Движение | Действие |
| --- | --- |
| Влево | Перейти на рабочий стол справа |
| Вправо | Перейти на рабочий стол слева |
| Вверх | Открыть «Представление задач» |
| Вниз | Закрыть «Представление задач» и вернуться к предыдущему окну |

Щёлкни правой кнопкой по значку в трее, чтобы отключить все жесты колёсиком или только жесты «Представления задач».

## Сборка из исходников

Из папки репозитория в PowerShell на 64-битной Windows:

```powershell
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /optimize+ /codepage:65001 /target:winexe '/out:macos controls.exe' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:AltQTray.ico /resource:AltQTray.ico,AltQTray.ico AltQTray.cs
```

## English

**macos controls** is a portable utility for Windows 10 and 11 that brings macOS-style app closing and virtual desktop gestures to Windows. [Download and run macos controls.exe](./macos%20controls.exe?raw=true).

Press **Alt+Q** to request a normal close for all windows owned by the active app’s process. Apps can still prompt you to save changes or keep running in the system tray. If PowerToys or AutoHotkey already uses Alt+Q, remove the conflicting binding before launching the utility.

### Mouse gestures

Hold down the mouse wheel (middle button), move the mouse at least 56 pixels horizontally or vertically, and release the button.

| Move | Action |
| --- | --- |
| Left | Switch to the desktop on the right |
| Right | Switch to the desktop on the left |
| Up | Open Task View to see your open windows and virtual desktops |
| Down | Close Task View and return to the previous window |

Right-click the tray icon to pause Alt+Q, toggle all mouse gestures, disable only the Task View gestures, or exit the utility.

To build from source, run the PowerShell commands above on 64-bit Windows.
