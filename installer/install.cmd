@echo off
setlocal
chcp 65001 >nul
set "CROWLINK_TARGET=%LOCALAPPDATA%\Programs\CrowLink"
set "CROWLINK_START=%APPDATA%\Microsoft\Windows\Start Menu\Programs\CrowLink"

if not exist "%CROWLINK_TARGET%" mkdir "%CROWLINK_TARGET%"
if not exist "%CROWLINK_START%" mkdir "%CROWLINK_START%"

copy /y "CrowLink.exe" "%CROWLINK_TARGET%\CrowLink.exe" >nul || goto :error
copy /y "CrowLink-1.5-Manual-KO.html" "%CROWLINK_TARGET%\CrowLink-1.5-Manual-KO.html" >nul || goto :error
copy /y "LICENSE" "%CROWLINK_TARGET%\LICENSE.txt" >nul || goto :error
copy /y "CrowLink-Uninstall.cmd" "%CROWLINK_TARGET%\CrowLink-Uninstall.cmd" >nul || goto :error

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$shell=New-Object -ComObject WScript.Shell; $app='%CROWLINK_TARGET%\CrowLink.exe'; $start='%CROWLINK_START%'; $shortcut=$shell.CreateShortcut((Join-Path $start 'CrowLink 1.5.lnk')); $shortcut.TargetPath=$app; $shortcut.WorkingDirectory='%CROWLINK_TARGET%'; $shortcut.Description='CrowLink 1.5'; $shortcut.Save(); $manual=$shell.CreateShortcut((Join-Path $start 'CrowLink 사용 설명서.lnk')); $manual.TargetPath='%CROWLINK_TARGET%\CrowLink-1.5-Manual-KO.html'; $manual.Save(); $desktop=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'CrowLink 1.5.lnk')); $desktop.TargetPath=$app; $desktop.WorkingDirectory='%CROWLINK_TARGET%'; $desktop.Description='CrowLink 1.5'; $desktop.Save(); $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CrowLink'; New-Item -Path $key -Force | Out-Null; New-ItemProperty -Path $key -Name DisplayName -Value 'CrowLink 1.5' -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name DisplayVersion -Value '1.5.0' -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name Publisher -Value 'CrowScienceLab' -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name DisplayIcon -Value $app -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name InstallLocation -Value '%CROWLINK_TARGET%' -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name UninstallString -Value ('cmd.exe /c ""%CROWLINK_TARGET%\CrowLink-Uninstall.cmd""') -PropertyType String -Force | Out-Null; New-ItemProperty -Path $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -Path $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null" || goto :error

start "" "%CROWLINK_TARGET%\CrowLink.exe"
exit /b 0

:error
echo CrowLink 설치 중 오류가 발생했습니다. CrowLink가 실행 중이면 종료한 뒤 다시 시도하세요.
pause
exit /b 1
