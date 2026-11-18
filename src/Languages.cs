using System.Globalization;

namespace ServerFavorites;

internal static class Languages
{
    public static IReadOnlyList<(string Code, string Label)> Choices { get; } =
        [("zh-TW", "繁體中文"), ("zh-CN", "简体中文"), ("en", "English")];

    public static string Normalize(string? code) => Choices.Any(choice => choice.Code == code) ? code! : "zh-TW";

    private static readonly Dictionary<string, (string Traditional, string Simplified, string English)> Labels = new()
    {
        ["AppTitle"] = ("伺服器收藏", "服务器收藏", "Server Manager"),
        ["Groups"] = ("收藏分組", "收藏分组", "Server groups"),
        ["AddGroup"] = ("新增分組", "新增分组", "Add group"),
        ["AddCategory"] = ("新增類別", "新增类别", "Add category"),
        ["AddGroupHere"] = ("在此類別新增分組", "在此类别新增分组", "Add group to this category"),
        ["RenameCategory"] = ("重新命名類別", "重命名类别", "Rename category"),
        ["DeleteCategory"] = ("刪除類別", "删除类别", "Delete category"),
        ["CategoryName"] = ("類別名稱", "类别名称", "Category name"),
        ["CategoryRequired"] = ("請輸入類別名稱。", "请输入类别名称。", "Enter a category name."),
        ["CategoryDuplicate"] = ("已有同名類別。", "已有同名类别。", "A category with this name already exists."),
        ["DeleteCategoryPrompt"] = ("刪除「{0}」類別？組內分組與收藏會保留，分組將移回最外層。", "删除“{0}”类别？组内分组和收藏会保留，分组将移回最外层。", "Delete the “{0}” category? Its groups will move to the top level with their favorites."),
        ["Search"] = ("搜尋伺服器", "搜索服务器", "Search servers"),
        ["RefreshGroup"] = ("重新整理目前分組", "刷新当前分组", "Refresh current group"),
        ["AddServer"] = ("新增伺服器", "添加服务器", "Add server"),
        ["Join"] = ("加入伺服器", "加入服务器", "Join server"),
        ["ViewDetails"] = ("查看詳細資訊", "查看详细信息", "View details"),
        ["CopyAddress"] = ("複製位址", "复制地址", "Copy address"),
        ["CopyLink"] = ("複製連線連結", "复制连接链接", "Copy join link"),
        ["EditAlias"] = ("編輯伺服器／暱稱", "编辑服务器／昵称", "Edit server / alias"),
        ["Pin"] = ("置頂", "置顶", "Pin"),
        ["Unpin"] = ("取消置頂", "取消置顶", "Unpin"),
        ["MoveTo"] = ("移至分組", "移至分组", "Move to group"),
        ["RemoveFavorite"] = ("移除收藏", "移除收藏", "Remove favorite"),
        ["Name"] = ("伺服器名稱", "服务器名称", "Server name"),
        ["Map"] = ("地圖", "地图", "Map"),
        ["Players"] = ("人數", "人数", "Players"),
        ["Latency"] = ("延遲", "延迟", "Latency"),
        ["CloseDetails"] = ("關閉詳細資訊", "关闭详细信息", "Close details"),
        ["Details"] = ("伺服器詳細資訊", "服务器详细信息", "Server details"),
        ["Copy"] = ("複製", "复制", "Copy"),
        ["DetailPlayers"] = ("玩家", "玩家", "Players"),
        ["OnlinePlayers"] = ("線上玩家", "在线玩家", "Online players"),
        ["RefreshDetails"] = ("只重新整理這台伺服器", "仅刷新这台服务器", "Refresh this server only"),
        ["PlayerName"] = ("玩家名稱", "玩家名称", "Player name"),
        ["DeleteGroup"] = ("刪除分組", "删除分组", "Delete group"),
        ["Delete"] = ("刪除", "删除", "Delete"),
        ["DeleteEmptyGroup"] = ("刪除「{0}」分組？", "删除“{0}”分组？", "Delete the “{0}” group?"),
        ["DeleteGroupWithOneServer"] = ("刪除「{0}」分組？組內的 {1} 台伺服器收藏也會移除。", "删除“{0}”分组？组内的 {1} 台服务器收藏也会移除。", "Delete the “{0}” group and its {1} saved server?"),
        ["DeleteGroupWithServers"] = ("刪除「{0}」分組？組內的 {1} 台伺服器收藏也會移除。", "删除“{0}”分组？组内的 {1} 台服务器收藏也会移除。", "Delete the “{0}” group and its {1} saved servers?"),
        ["CreateGroupFirst"] = ("請先建立分組，再新增伺服器。", "请先建立分组，再添加服务器。", "Create a group before adding servers."),
        ["RenameGroup"] = ("重新命名分組", "重命名分组", "Rename group"),
        ["Count"] = ("{0} 台伺服器", "{0} 台服务器", "{0} servers"),
        ["CountOne"] = ("{0} 台伺服器", "{0} 台服务器", "{0} server"),
        ["NoFavorites"] = ("尚無收藏伺服器", "暂无收藏服务器", "No favorite servers yet"),
        ["NoMatches"] = ("找不到符合的伺服器", "找不到匹配的服务器", "No matching servers"),
        ["SteamRequest"] = ("已交給 Steam 處理連線：{0}", "已交给 Steam 处理连接：{0}", "Connection sent to Steam: {0}"),
        ["SteamError"] = ("無法開啟 Steam：{0}", "无法打开 Steam：{0}", "Could not open Steam: {0}"),
        ["GroupName"] = ("分組名稱", "分组名称", "Group name"),
        ["Add"] = ("新增", "添加", "Add"),
        ["Save"] = ("儲存", "保存", "Save"),
        ["GroupRequired"] = ("請輸入分組名稱。", "请输入分组名称。", "Enter a group name."),
        ["GroupDuplicate"] = ("已有同名分組。", "已有同名分组。", "A group with this name already exists."),
        ["EditServer"] = ("編輯伺服器", "编辑服务器", "Edit server"),
        ["Address"] = ("伺服器位址", "服务器地址", "Server address"),
        ["Alias"] = ("暱稱（選填）", "昵称（选填）", "Alias (optional)"),
        ["AddressHint"] = ("例如 example.com:27015", "例如 example.com:27015", "For example, example.com:27015"),
        ["AddressInvalid"] = ("請輸入有效的伺服器位址，例如 127.0.0.1:27015。", "请输入有效的服务器地址，例如 127.0.0.1:27015。", "Enter a valid server address, such as 127.0.0.1:27015."),
        ["DuplicateFavorite"] = ("這台伺服器已在收藏中。", "这台服务器已在收藏中。", "This server is already in this group."),
        ["Copied"] = ("已複製", "已复制", "Copied"),
        ["ClipboardError"] = ("暫時無法使用剪貼簿", "暂时无法使用剪贴板", "Clipboard unavailable"),
        ["Remove"] = ("移除", "移除", "Remove"),
        ["RemovePrompt"] = ("移除「{0}」？", "移除“{0}”？", "Remove “{0}”?"),
        ["NoResponse"] = ("伺服器未回應", "服务器未响应", "Server did not respond"),
        ["StaleData"] = ("伺服器未回應 · 顯示上次查詢資料", "服务器未响应 · 显示上次查询数据", "No response · Showing last update"),
        ["MapNoResponse"] = ("未回應", "未响应", "No response"),
        ["MapStale"] = ("{0}（上次查詢）", "{0}（上次查询）", "{0} (last update)"),
        ["LoadingPlayers"] = ("正在取得玩家名單…", "正在获取玩家列表…", "Loading player list…"),
        ["PlayerListUnavailable"] = ("無法取得玩家名單", "无法获取玩家列表", "Player list unavailable"),
        ["NoPlayers"] = ("目前無人在線", "目前无人在线", "No players online"),
        ["Refreshing"] = ("正在更新伺服器狀態…", "正在更新服务器状态…", "Refreshing servers…"),
        ["NoPlayerName"] = ("（未提供名稱）", "（未提供名称）", "(No name provided)"),
        ["Cancel"] = ("取消", "取消", "Cancel"),
        ["LoadError"] = ("無法讀取收藏。請先備份設定檔後再重開。", "无法读取收藏。请先备份配置文件后再打开。", "Could not read favorites. Back up the settings file before reopening."),
        ["SaveError"] = ("儲存失敗：{0}", "保存失败：{0}", "Could not save: {0}"),
        ["Language"] = ("介面語言", "界面语言", "Interface language"),
    };

    public static IEnumerable<string> Keys => Labels.Keys;

    public static string Text(string language, string key, params object[] values)
    {
        var label = Labels[key];
        string text = language switch { "en" => label.English, "zh-CN" => label.Simplified, _ => label.Traditional };
        return values.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, values);
    }
}
