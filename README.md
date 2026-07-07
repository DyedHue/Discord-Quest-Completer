<img width="1400" height="818" alt="Illustratifddfon" src="https://github.com/user-attachments/assets/af963964-9885-4ae9-b5a7-a0f91884e69d" />

# What is it?  
Discord detects you playing a game as long as an exe is running from the path where the game would be at with the same exe name.  
With this knowledge, you can complete Discord's 'play' quests without installing the game/app by creating the folders and putting in a dummy exe.  
**This app helps you automate the task of creating a dummy exe in your desired path and run it.**  
  
For example, Wuthering waves' exe path is:  
any path\ `Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe`.  
You just have to input this path and the app will create the folders with a dummy exe inside of:  
The app's directory\Discord Quest Completer\DQC Game Folders\\..

# How to use
1. Fill in the EXE Path field with the proper path structure of the game. ([How to find path?](#How-to-find-path))  
e.g., `Where Winds Meet/Engine/Binaries/Win64/wwm.exe`  
2. Click 'Create Executable' to generate a dummy game.  
3. Click 'Start' from your list of games and the quest should start progressing.

# How to find path
You can type the name of a game in the "Search and auto fill fields" section and click 'Search and fill'.  
This will search for the game's path from your [local database](#Local-Database) and Steam and auto fill the Name and EXE Path fields if found.  
If you can't find the correct path this way, you can try searching on the r/DiscordQuests subreddit or Google from the links on the app.

# Local Database
The `game-index.json` file located at the app's directory is your local database.
- It's taken from https://www.reddit.com/r/DiscordQuests/wiki/game-index.json  
- It does NOT auto update as of yet.  
- You have to manually update it like this:
    1. Go to the page by following the link above or from the link in the app.
    2. Press Ctrl + S and save it to the app's directory (Press the button on the app to open the directory). Replace if it already exists.