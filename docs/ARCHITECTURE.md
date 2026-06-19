# ModListHider — 项目架构

版本 0.3.2 | 更新日期 2026-06-19

---

## 项目概览

**ModListHider** 是杀戮尖塔 2 联机模组管理增强工具。核心功能：

| 功能 | 描述 |
|------|------|
| **小眼睛（Per-Mod Hide）** | 每行 Mod 右侧注入眼睛图标，点击切换该 Mod 在联机列表中的可见性 |
| **大眼睛（Vanilla Mode）** | 模组页面左上角全局开关，开启后联机握手报告"无 Mod"，伪装成原版客户端 |

技术栈：.NET 9 + Godot 4.5.1 C# + Harmony 2.3.3

---

## 模块结构

```
src/
├── ModuleInit.cs             入口：DLL 加载时自动执行
├── Stubs.cs                  编译用游戏类型占位符
├── Config/
│   └── ModListHiderConfig.cs  持久化配置（JSON 读写）
├── Core/
│   ├── DebugLog.cs            调试日志开关
│   ├── ModListFilterPatch.cs  拦截 ModManager.GetGameplayRelevantModNameList()
│   └── InitialGameInfoFilterPatch.cs  拦截 InitialGameInfoMessage 构造
└── UI/
    ├── IconResourcePaths.cs   图标路径常量
    ├── HideIconNode.cs        小眼睛节点（绘制+点击+布局）
    ├── VanillaModeToggleNode.cs  大眼睛节点（绘制+点击+Tooltip）
    ├── ModMenuRowPatch.cs     ModMenuRowIconInjector：持续扫描并注入小眼睛
    ├── VanillaModeTogglePatch.cs  VanillaModeToggleInjector：持续扫描并注入大眼睛
    ├── DebugHotkeyWatcher.cs  Ctrl+Shift+F8 切换 Debug 模式
    └── NHideIcon.cs          编译占位（实际实现为 HideIconNode）
```

---

## 数据流

```
┌─────────────────────────────────────────────────────────────────────┐
│                         ModListHider 数据流                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. ModuleInit.Initialize()    [DLL 加载时触发]                      │
│     │                                                               │
│     ├── ModListHiderConfig.Load() ─── 读取 hidden_mods.json         │
│     │                                                               │
│     ├── 注入 Node[CallDeferred]:                                     │
│     │   ├── VanillaModeToggleInjector → sceneTree.Root              │
│     │   ├── ModMenuRowIconInjector  → sceneTree.Root                │
│     │   └── DebugHotkeyWatcher      → sceneTree.Root                │
│     │                                                               │
│     └── Harmony 自动 Patch（通过 [HarmonyPatch] 特性发现）           │
│                                                                     │
│  2. UI 注入层（_Process 持续扫描）                                   │
│     │                                                               │
│     ModMenuRowIconInjector (每 0.25s):                               │
│     ├── FindModdingScreen() → 递归搜索 NModdingScreen                │
│     ├── FindModMenuRows()   → 找带 Title+Tickbox+Mod 属性的行        │
│     ├── ResolveModIdentity() → 读 manifest.id（稳定 ID）或 Title     │
│     ├── CleanupOrphanIcons() → 移除挂到消失行的图标                   │
│     └── HideIconNode.ConfigureIcon() → 注入图标到行                   │
│                                                                     │
│     VanillaModeToggleInjector (每 0.35s):                            │
│     ├── FindModdingScreen() → 递归搜索 NModdingScreen                │
│     └── VanillaModeToggleNode.Configure() → 注入到左上角             │
│                                                                     │
│  3. 用户点击 → 持久化                                                │
│     HideIconNode._GuiInput()                                        │
│     ├── ToggleHidden(modId)                                         │
│     ├── Config.Save() ─── 写入 hidden_mods.json                      │
│     └── PlayClickSound()                                            │
│                                                                     │
│     VanillaModeToggleNode._GuiInput()                                │
│     ├── SetVanillaMode(on)                                          │
│     └── Config.Save() ─── 写入 vanilla_mode 字段                     │
│                                                                     │
│  4. 过滤层（Harmony Patch → 联机数据）                               │
│                                                                     │
│     ModListFilterPatch.Postfix()                                    │
│     └── VanillaMode ON?  → Clear() 返回空列表                        │
│         VanillaMode OFF? → RemoveAll(ShouldStripFromMultiplayerList) │
│                                                                     │
│     InitialGameInfoFilterPatch.Postfix()                             │
│     └── 反射递归遍历 payload 对象                                    │
│         → 找到 mod 相关字段中的 List<string>                          │
│         → ShouldStripFromMultiplayerList 过滤                        │
│                                                                     │
│  5. ShouldStripFromMultiplayerList(entry)                           │
│     匹配逻辑：                                                       │
│     ├── entry 精确匹配 HiddenModIds                                  │
│     ├── entry 去掉 -version 后缀后匹配 HiddenModIds                  │
│     └── entry 以 "hiddenId-" 开头且后缀为合法版本号 → 命中            │
│                                                                     │
│     关键：entry 格式是 "ManifestId-1.0.0"（来自 GetGameplayRelevant   │
│           ModNameList），HiddenModIds 存的是 manifest.id（无版本）    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 依赖关系

```
ModuleInit
  ├── ModListHiderConfig (Load/Save/Reload)
  ├── VanillaModeToggleInjector (Node, 挂到 sceneTree.Root)
  │     └── VanillaModeToggleNode (Control, 注入到 NModdingScreen)
  ├── ModMenuRowIconInjector (Node, 挂到 sceneTree.Root)
  │     └── HideIconNode (Control, 注入到 NModMenuRow)
  │           └── IconResourcePaths
  └── DebugHotkeyWatcher (Node, 挂到 sceneTree.Root)

Harmony Patches (无实例化依赖, _Process 持续生效):
  ├── ModListFilterPatch → ModListHiderConfig
  └── InitialGameInfoFilterPatch → ModListHiderConfig

Stubs (仅编译时可用):
  └── 为 Harmony TargetMethods() 和反射查找提供类型引用
```

---

## 文件清单

| 文件 | 行数 | 角色 |
|------|------|------|
| `src/ModuleInit.cs` | 148 | DLL 入口，注入器创建 |
| `src/Config/ModListHiderConfig.cs` | 335 | JSON 配置持久化 + 键匹配逻辑 |
| `src/Core/ModListFilterPatch.cs` | 97 | 拦截 MP mod 列表（Vanilla + 单 Mod） |
| `src/Core/InitialGameInfoFilterPatch.cs` | 262 | 拦截 Lobby 建房间 payload |
| `src/Core/DebugLog.cs` | 22 | Debug 模式条件日志 |
| `src/UI/ModMenuRowPatch.cs` | 407 | 持续扫描 + 注入小眼睛图标 |
| `src/UI/HideIconNode.cs` | 394 | 小眼睛绘制/点击/定位 |
| `src/UI/VanillaModeTogglePatch.cs` | 147 | 持续扫描 + 注入大眼睛图标 |
| `src/UI/VanillaModeToggleNode.cs` | 179 | 大眼睛绘制/点击/多语言 Tooltip |
| `src/UI/IconResourcePaths.cs` | 19 | 图标资源路径常量 |
| `src/UI/DebugHotkeyWatcher.cs` | 25 | Ctrl+Shift+F8 调试开关 |
| `src/UI/NHideIcon.cs` | 25 | 编译占位 |
| `src/Stubs.cs` | 109 | 游戏类型编译占位 |
| `build.ps1` | 80 | 构建脚本 |
| `mod_manifest.json` | 11 | Mod 清单（v0.3.2） |

---

## 关键版本记录

| 版本 | 主要变更 |
|------|----------|
| v0.2.x | 首次实现小眼睛（Per-Mod Hide） |
| v0.3.0 | 新增大眼睛（Vanilla Mode）+ InitialGameInfoFilterPatch |
| v0.3.1 | 切换到 [ModuleInitializer] 入口（修复 Android 端失效） |
| v0.3.2 | 修复键匹配（稳定 ID 优先）+ 注入器改为持久化扫描模式 |