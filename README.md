# 🎬 ShowStasher

## 📄 Description

A desktop WPF application that helps you organize, rename, and categorize your collection of movies, TV shows, and anime. ShowStasher fetches metadata from TMDb and Jikan, renames files accordingly, groups them into folders, and caches metadata locally for offline access.

Whether you're tidying up a messy media folder or maintaining a pristine archive, ShowStasher's automation has your back.

## ✨ Key Features

-	🧠 Smart Filename Parsing – Automatically detects titles, seasons, and episodes from filenames
-	🌐 Metadata Fetching – Uses TMDb for general content and Jikan for anime
- 	🗂 Folder Organization – Sorts content into structured folders by title and season
-	💾 Offline Support – Caches metadata in SQLite for speed and reliability
-	🪪 API Key Prompt – Handy dialog to securely enter and store your TMDb key
-	🧪 Dry Run Preview – Visualize the folder layout before making any file changes
-	🕘 History Viewer – Browse and delete logs of moved files
-	🎨 Polished UI – Metro-styled interface built with MahApps.Metro		

## 🛠️ Technologies Used
-	C# (.NET 8)
-	WPF (MVVM Pattern)
-	CommunityToolkit.Mvvm
-	SQLite
-	MahApps.Metro
-	TMDb API
-	Jikan API


## 📦 Installation
1. 	Clone the repository:
```bash
git clone https://github.com/ThePrince05/ShowStasher.git
```

2.	Open the project in Visual Studio 2022 or newer.
3. 	Build and run the app (F5) or publish it for distribution.
4.	On first run, enter your TMDb API Key when prompted.

✅ .NET Target: net8.0-windows  
✅ Minimum OS: Windows 10+ (x86)		

## 🧪 Usage
Watch a quick demo of the app in action:  
📺 [YouTube Demo](https://youtu.be/PBfi1NHm1hc)

The video covers:
- Setting up API keys
- Organizing a sample folder with movies and anime
- Using the dry-run preview
- Viewing and managing move history

⚠️ Note:

- "For series or anime, make sure filenames include tags like S01E01 so the app can detect season and episode numbers."

- "If a file isn’t picked up properly, try renaming it to a cleaner format - just the title or title with S01E01 works best."

## 🤝Contributing and Contacts
I welcome contributions, suggestions, or questions!
Feel free to reach out via email: princesithole49@gmail.com

## 📝 License
This project is licensed under the **[Apache License 2.0](LICENSE)**.
