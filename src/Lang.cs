using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeskFence
{
    /// <summary>界面文字：中文 / English / 日本語</summary>
    static class T
    {
        public static readonly string[] Codes = { "zh", "en", "ja" };
        public static readonly string[] NativeNames = { "中文", "English", "日本語" };

        static int cur = 0;
        static readonly Dictionary<string, string[]> map = new Dictionary<string, string[]>();

        static void A(string key, string zh, string en, string ja) { map[key] = new string[] { zh, en, ja }; }

        static T()
        {
            // 格子
            A("rename", "重命名", "Rename", "名前の変更");
            A("toIcon", "切换为图标模式", "Switch to icon view", "アイコン表示に切り替え");
            A("toList", "切换为列表模式", "Switch to list view", "リスト表示に切り替え");
            A("lock", "锁定位置", "Lock position", "位置をロック");
            A("unlock", "解锁位置", "Unlock position", "ロック解除");
            A("expand", "展开", "Expand", "展開");
            A("collapse", "折叠", "Collapse", "折りたたむ");
            A("sortName", "按名称排序", "Sort by name", "名前順に並べ替え");
            A("cleanMissing", "清理已不存在的项目", "Remove missing items", "存在しない項目を削除");
            A("newFence", "新建格子", "New fence", "新しいボックス");
            A("deleteFence", "删除这个格子（文件放回桌面）", "Delete this fence (items go back to desktop)", "このボックスを削除（ファイルはデスクトップに戻る）");
            A("opacity", "调整透明度...", "Adjust transparency...", "透明度の調整...");
            A("dropHint", "把桌面文件拖到这里", "Drag desktop files here", "デスクトップのファイルをここにドラッグ");
            A("missing", "（已不存在）", " (missing)", "（見つかりません）");
            A("defaultFirst", "我的格子", "My fence", "マイボックス");
            A("defaultNew", "新格子", "New fence", "新しいボックス");
            // 项目
            A("open", "打开", "Open", "開く");
            A("openLocation", "打开所在位置", "Open file location", "ファイルの場所を開く");
            A("removeItem", "移出格子（放回桌面显示）", "Remove from fence (show on desktop)", "ボックスから外す（デスクトップに戻す）");
            // 对话框
            A("namePrompt", "格子名称：", "Fence name:", "ボックス名：");
            A("ok", "确定", "OK", "OK");
            A("cancel", "取消", "Cancel", "キャンセル");
            A("fileMissing", "文件已不存在：", "File no longer exists:", "ファイルが見つかりません：");
            A("openFailed", "打开失败：", "Failed to open: ", "開けませんでした：");
            A("deleteAsk", "删除格子「{0}」？", "Delete fence \"{0}\"?", "ボックス「{0}」を削除しますか？");
            A("deleteAskItems", "删除格子「{0}」？\n里面的 {1} 个项目会放回桌面显示（文件本身不会删除）。",
                "Delete fence \"{0}\"?\nIts {1} item(s) will be shown on the desktop again (files are not deleted).",
                "ボックス「{0}」を削除しますか？\n中の {1} 個の項目はデスクトップに戻ります（ファイル自体は削除されません）。");
            A("alreadyRunning", "DeskFence 已经在运行了（右下角托盘里有蓝色方格图标）。",
                "DeskFence is already running (blue grid icon in the system tray).",
                "DeskFence は既に実行中です（タスクトレイの青いアイコン）。");
            A("autostartFailed", "设置开机启动失败：", "Failed to set auto start: ", "自動起動の設定に失敗しました：");
            A("opacityTitle", "调整透明度", "Transparency", "透明度の調整");
            A("bgOpacity", "背景不透明度", "Background opacity", "背景の不透明度");
            A("allOpacity", "整体不透明度（含图标和文字）", "Overall opacity (icons and text too)", "全体の不透明度（アイコンと文字を含む）");
            // 托盘
            A("trayTip", "DeskFence 桌面格子", "DeskFence desktop fences", "DeskFence デスクトップ整理");
            A("lockAll", "全部锁定", "Lock all", "すべてロック");
            A("unlockAll", "全部解锁", "Unlock all", "すべてロック解除");
            A("hideOriginals", "收纳后隐藏桌面上的原图标", "Hide original desktop icons of stored items", "収納したらデスクトップの元アイコンを隠す");
            A("autostart", "开机自动启动", "Start with Windows", "Windows 起動時に自動実行");
            A("language", "语言 / Language", "Language / 语言", "言語 / Language");
            A("hideTray", "隐藏托盘图标", "Hide tray icon", "トレイアイコンを隠す");
            A("trayHiddenInfo", "托盘图标已隐藏。\n\n以后打开设置：点格子标题栏右上角的齿轮按钮。\n想恢复托盘图标：再双击运行一次 DeskFence.exe，或在设置里取消勾选。",
                "The tray icon is now hidden.\n\nTo open settings later, click the gear button at the top right of any fence.\nTo bring the tray icon back, run DeskFence.exe again, or uncheck this option in settings.",
                "トレイアイコンを非表示にしました。\n\n設定はボックスのタイトルバー右上の歯車ボタンから開けます。\nトレイアイコンを戻すには DeskFence.exe をもう一度実行するか、設定でチェックを外してください。");
            A("trayRestored", "DeskFence 正在运行，托盘图标已恢复", "DeskFence is running. Tray icon restored.", "DeskFence は実行中です。トレイアイコンを再表示しました。");
            A("selfNoCollect", "DeskFence 程序本身不能放进格子，否则开机时找不到它，无法自动启动。\n\n建议：把 DeskFence 放在一个固定的文件夹里（比如 D:\\工具\\DeskFence），运行一次让开机启动指向那里；想放进格子的话，拖它的快捷方式进来。",
                "DeskFence itself can't be stored in a fence; otherwise Windows can't find it at startup and auto start stops working.\n\nTip: keep DeskFence in a fixed folder (e.g. D:\\Tools\\DeskFence), run it once from there, and drag a shortcut to it into a fence instead.",
                "DeskFence 本体はボックスに収納できません（起動時に見つからず、自動起動できなくなるため）。\n\nおすすめ：DeskFence を固定のフォルダー（例 D:\\Tools\\DeskFence）に置いて一度そこから実行し、ボックスにはショートカットを入れてください。");
            A("selfReleased", "已把 DeskFence 程序放回桌面：程序本身放在格子里会导致开机无法自动启动。",
                "DeskFence was moved back to the desktop: storing the program itself in a fence breaks auto start.",
                "DeskFence をデスクトップに戻しました：本体をボックスに入れると自動起動できなくなります。");
            A("renameFile", "重命名", "Rename", "名前の変更");
            A("renamePrompt", "新文件名：", "New file name:", "新しいファイル名：");
            A("renameBadName", "文件名不能包含下列字符：\\ / : * ? \" < > |", "A file name can't contain any of these characters: \\ / : * ? \" < > |", "ファイル名に次の文字は使えません：\\ / : * ? \" < > |");
            A("renameExists", "已经有同名的文件了。", "A file with that name already exists.", "同じ名前のファイルが既にあります。");
            A("renameFailed", "重命名失败：", "Rename failed: ", "名前を変更できませんでした：");
            A("findMissing", "查找丢失的文件（修复\"已不存在\"）", "Find missing files (fix \"missing\")", "見つからないファイルを探す");
            A("openStore", "打开存放文件夹", "Open storage folder", "保管フォルダーを開く");
            A("findResult", "找回 {0} 个文件，仍有 {1} 个找不到。\n\n存放文件夹：{2}",
                "Found {0} file(s); {1} still missing.\n\nStorage folder: {2}",
                "{0} 個のファイルを見つけました。{1} 個はまだ見つかりません。\n\n保管フォルダー：{2}");
            A("locations", "文件位置", "File locations", "ファイルの場所");
            A("storeDir", "存放文件夹：", "Storage folder: ", "保管フォルダー：");
            A("configDir", "配置文件夹：", "Settings folder: ", "設定フォルダー：");
            A("changeStore", "更改存放文件夹...", "Change storage folder...", "保管フォルダーを変更...");
            A("changeConfig", "更改配置文件位置...", "Change settings location...", "設定ファイルの場所を変更...");
            A("openLog", "打开日志文件夹", "Open log folder", "ログフォルダーを開く");
            A("resetLocations", "恢复默认位置", "Restore default locations", "既定の場所に戻す");
            A("pickStore", "选择存放收纳文件的文件夹", "Choose the folder that holds stored files", "収納したファイルを置くフォルダーを選択");
            A("pickConfig", "选择保存配置文件（config.xml）的文件夹", "Choose the folder for the settings file (config.xml)", "設定ファイル（config.xml）を保存するフォルダーを選択");
            A("storeInDesktop", "存放文件夹不能放在桌面里，否则收纳的文件会重新出现在桌面上。请换一个位置。",
                "The storage folder can't be inside the desktop, or stored files would show up on the desktop again. Please choose another folder.",
                "保管フォルダーをデスクトップ内に置くことはできません（収納したファイルがデスクトップに再表示されるため）。別の場所を選んでください。");
            A("confirmStore", "把存放文件夹改为：\n{0}\n\n已收纳的文件会一起搬过去。继续吗？",
                "Change the storage folder to:\n{0}\n\nFiles already stored will be moved there. Continue?",
                "保管フォルダーを次に変更します：\n{0}\n\n収納済みのファイルも移動します。続けますか？");
            A("crossDriveWarn", "注意：新位置和桌面不在同一个盘。以后每次收纳/放回都要真正复制文件，大文件或文件夹会慢一些。",
                "Note: the new folder is on a different drive from the desktop. Storing and returning files will copy the data each time, which is slower for large files or folders.",
                "注意：新しい場所はデスクトップと別のドライブです。収納・戻すたびに実際にコピーするため、大きなファイルやフォルダーは時間がかかります。");
            A("storeChanged", "存放文件夹已改为：\n{0}", "Storage folder changed to:\n{0}", "保管フォルダーを変更しました：\n{0}");
            A("storeSomeFailed", "以下文件没能搬过去（可能正在使用），仍在原来的文件夹 {1}，照常可用：\n{0}",
                "These files couldn't be moved (maybe in use). They stay in the old folder {1} and still work:\n{0}",
                "次のファイルは移動できませんでした（使用中の可能性）。元のフォルダー {1} に残り、そのまま使えます：\n{0}");
            A("configExists", "目标位置已经有一个 config.xml：\n{0}\n\n要用当前设置覆盖它吗？",
                "There is already a config.xml at:\n{0}\n\nOverwrite it with the current settings?",
                "移動先に config.xml が既にあります：\n{0}\n\n現在の設定で上書きしますか？");
            A("configChanged", "配置文件已移到：\n{0}", "Settings file moved to:\n{0}", "設定ファイルを移動しました：\n{0}");
            A("moveFailed", "操作失败：", "Operation failed: ", "失敗しました：");
            A("layouts", "布局", "Layouts", "レイアウト");
            A("saveLayout", "保存当前布局...", "Save current layout...", "現在のレイアウトを保存...");
            A("layoutName", "布局名称：", "Layout name:", "レイアウト名：");
            A("restoreLayout", "恢复这个布局", "Restore this layout", "このレイアウトに戻す");
            A("deleteLayout", "删除", "Delete", "削除");
            A("autoLayout", "自动备份 ", "Auto backup ", "自動バックアップ ");
            A("noLayouts", "（还没有保存的布局）", "(no saved layouts yet)", "（保存されたレイアウトはありません）");
            A("layoutSaved", "已保存布局「{0}」", "Layout \"{0}\" saved", "レイアウト「{0}」を保存しました");
            A("openConfig", "打开配置文件夹", "Open settings folder", "設定フォルダーを開く");
            A("exit", "退出（收纳的文件放回桌面）", "Exit (stored files go back to desktop)", "終了（収納したファイルをデスクトップに戻す）");
            A("exitKeep", "退出（文件不恢复到桌面）", "Exit (keep files in fences)", "終了（ファイルをデスクトップに戻さない）");
            A("collectMode", "收纳桌面文件（移入格子）", "Store desktop files in fences", "デスクトップのファイルを収納");
            A("cantCollect", "以下文件正在使用或没有权限，暂时留在桌面（关闭后会自动收进来）：\n{0}",
                "These files are in use or not accessible and stay on the desktop for now (stored automatically once closed):\n{0}",
                "次のファイルは使用中またはアクセス権がないため、デスクトップに残ります（閉じると自動で収納されます）：\n{0}");
            A("cantRelease", "以下文件正在使用，无法放回桌面，请先关闭它：\n{0}",
                "These files are in use and can't be moved back to the desktop. Close them first:\n{0}",
                "次のファイルは使用中のためデスクトップに戻せません。先に閉じてください：\n{0}");
            A("restoreLater", "以下文件正在使用，没能放回桌面，仍在存放文件夹里（下次启动 DeskFence 会照常显示在格子里）：\n{0}",
                "These files are in use and couldn't be moved back to the desktop. They stay in the storage folder and will show in the fences next time DeskFence starts:\n{0}",
                "次のファイルは使用中のためデスクトップに戻せませんでした。保管フォルダーに残り、次回 DeskFence 起動時にボックスに表示されます：\n{0}");
            A("exitRestore", "退出并把收纳的图标放回桌面", "Exit and show stored icons on desktop", "終了してアイコンをデスクトップに戻す");
        }

        /// <summary>设置语言；空字符串 = 跟随系统</summary>
        public static void Set(string code)
        {
            if (string.IsNullOrEmpty(code)) code = SystemCode();
            int i = Array.IndexOf(Codes, code);
            cur = i < 0 ? 1 : i;
        }

        public static string Current { get { return Codes[cur]; } }

        public static string SystemCode()
        {
            string two = "en";
            try { two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; } catch { }
            if (two == "zh") return "zh";
            if (two == "ja") return "ja";
            return "en";
        }

        public static string S(string key)
        {
            string[] v;
            if (map.TryGetValue(key, out v)) return v[cur];
            return key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(S(key), args);
        }

        /// <summary>自测用：检查每个词条三种语言都有</summary>
        public static int CountIncomplete()
        {
            int n = 0;
            foreach (KeyValuePair<string, string[]> kv in map)
                foreach (string s in kv.Value) if (string.IsNullOrEmpty(s)) n++;
            return n;
        }
    }
}
