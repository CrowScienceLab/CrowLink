@echo off
setlocal
chcp 65001 >nul
set "CROWLINK_TARGET=%LOCALAPPDATA%\Programs\CrowLink"
set "CROWLINK_START=%APPDATA%\Microsoft\Windows\Start Menu\Programs\CrowLink"

taskkill /IM CrowLink.exe /F >nul 2>nul
del /q "%USERPROFILE%\Desktop\CrowLink 1.5.lnk" >nul 2>nul
rmdir /s /q "%CROWLINK_START%" >nul 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CrowLink" /f >nul 2>nul
cd /d "%TEMP%"
start "CrowLink 제거" /min cmd.exe /c "timeout /t 2 /nobreak ^>nul ^& rmdir /s /q ""%CROWLINK_TARGET%"""
exit /b 0
