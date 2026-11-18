# Server Manager 伺服器管理工具

[English](README.md)

用於整理及連線 Steam 遊戲伺服器的 Windows 桌面工具。支援 Valve A2S 查詢的伺服器可顯示即時資訊。

- 依地址收藏伺服器，並用分組整理清單。
- 查看伺服器提供的延遲、地圖和玩家資訊。
- 透過 Steam 連線到選取的伺服器。

## 使用方式

從 GitHub 儲存庫的 Releases 頁面下載 Windows ZIP。安裝 .NET 8 Desktop Runtime，解壓縮後開啟 `ServerManager.exe`。請將解壓後的檔案放在同一個資料夾。透過工具連線時需要 Steam。

## 編譯

在 Windows 上安裝 .NET 8 SDK，從儲存庫根目錄執行：

```powershell
dotnet build src/ServerFavorites.csproj -c Release
```

收藏清單存在 `%LOCALAPPDATA%\ServerFavorites\favorites.json`。
