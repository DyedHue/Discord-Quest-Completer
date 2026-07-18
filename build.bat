@echo off
echo Building Discord Quest Completer as a single standalone executable...
echo.

dotnet publish DiscordQuestCompleter.csproj -c Release -r win-x64 -p:PublishSingleFile=true

echo.
echo Build complete! Your single .exe file is located in:
echo bin\Release\net8.0-windows\win-x64\publish\
pause
