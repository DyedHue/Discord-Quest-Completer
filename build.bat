@echo off
echo Building Discord Quest Completer (Targeting .NET Framework 4.8)...
echo.

dotnet build DiscordQuestCompleter.csproj -c Release

echo.
echo Build complete! Your compiled .exe file is located in:
echo bin\Release\net48\
pause
