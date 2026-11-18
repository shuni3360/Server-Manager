# Server Manager

[繁體中文](README.zh-TW.md)

A Windows desktop app for organizing and connecting to Steam game servers. Live server information is available for servers that support Valve's A2S queries.

- Save servers by address and organize them into groups.
- Check latency, map, and player information when the server provides it.
- Connect to a selected server through Steam.

## Run

Download the Windows ZIP from the repository's Releases tab. Install the .NET 8 Desktop Runtime, extract the ZIP, and run `ServerManager.exe`. Keep the extracted files together. Steam is required to connect from the app.

## Build

On Windows with the .NET 8 SDK, run from the repository root:

```powershell
dotnet build src/ServerFavorites.csproj -c Release
```

Favorites are stored in `%LOCALAPPDATA%\ServerFavorites\favorites.json`.
