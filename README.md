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
| Вверх | Открыть «Представление задач». Повтори жест, чтобы вернуться к активному окну |
| Вниз | Показать рабочий стол. Повтори жест, чтобы восстановить окна. В «Представлении задач» жест возвращает к предыдущему окну |

Щёлкни правой кнопкой по значку в трее, чтобы отдельно отключить Alt+Q, жест вверх, жест вниз или переключение столов влево и вправо. Галочка означает, что действие отключено.
В пункте «Мини-справка» можно посмотреть сочетание клавиш и жесты.

## Автозапуск при входе в Windows

1. Сохрани `macos controls.exe` в папке, где он будет храниться постоянно.
2. Нажми **Win+R**, введи `shell:startup` и нажми **Enter**. Откроется папка автозагрузки текущего пользователя.
3. В открывшейся папке создай ярлык на `macos controls.exe` через **ПКМ → Создать → Ярлык**.

При следующем входе в Windows утилита запустится автоматически. Чтобы отключить автозапуск, удали ярлык из этой папки. [Инструкция Microsoft](https://support.microsoft.com/en-gb/windows/experience/startup-boot/configure-startup-applications-in-windows)

## Сборка из исходников

Из папки репозитория в PowerShell на 64-битной Windows:

```powershell
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /optimize+ /codepage:65001 /target:winexe '/out:macos controls.exe' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:MacosControls.ico /resource:MacosControls.ico,MacosControls.ico MacosControls.cs
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
| Up | Open Task View. Repeat to return to the active window |
| Down | Show the desktop. Repeat to restore the windows. In Task View, return to the previous window |

Right-click the tray icon to disable Alt+Q, the upward gesture, the downward gesture, or horizontal desktop switching independently. A checkmark means the action is disabled.
The «Мини-справка» (Quick help) submenu lists the controls.

### Start automatically at sign-in

1. Save `macos controls.exe` in a folder where you plan to keep it.
2. Press **Win+R**, type `shell:startup`, and press **Enter** to open the current user's Startup folder.
3. Right-click inside the folder and select **New → Shortcut**. Point the shortcut to `macos controls.exe`.

The utility will start the next time you sign in to Windows. To disable automatic startup, delete the shortcut from that folder. [Microsoft instructions](https://support.microsoft.com/en-gb/windows/experience/startup-boot/configure-startup-applications-in-windows)

To build from source, run the PowerShell commands above on 64-bit Windows.
