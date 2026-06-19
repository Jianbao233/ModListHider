# ModListHider — 实现逻辑

版本 0.3.2 | 更新日期 2026-06-19

---

## 1. 启动序列

```
DLL Loaded
  → [ModuleInitializer] ModuleInit.Initialize()
    → GD.Print("[ModListHider] ModuleInit.Initialize() called!")
    → ModListHiderConfig.Instance.Load()
      → 读取 %APPDATA%/SlayTheSpire2/ModListHider/hidden_mods.json
      → 若文件不存在：尝试从 NoClientCheats 配置迁移
    → 统计 [HarmonyPatch] 数量
    → CallDeferred() × 3：
      1. 创建 VanillaModeToggleInjector → sceneTree.Root
      2. 创建 ModMenuRowIconInjector  → sceneTree.Root
      3. 创建 DebugHotkeyWatcher       → sceneTree.Root
```

Harmony patch 由运行时自动发现并应用（`[HarmonyPatch]` 特性），无需手动调用。

---

## 2. 小眼睛注入器：ModMenuRowIconInjector

### 2.1 生命周期

```
_Ready()
  → GD.Print("started (persistent mode)")
  → Config.Load()

_Process(delta) 每 0.25s:
  → TryInjectIcons()
```

### 2.2 TryInjectIcons() 详细流程

```
TryInjectIcons()
├── FindModdingScreen(Root)              递归搜索 NModdingScreen
│   ├── 优：FindModdingScreenByType()    type.Name == "NModdingScreen"
│   └── 备：FindModdingScreenBySignature()  FindChild("InstalledModsTitle") && FindChild("ModsBorder")
├── FindModMenuRows(screen)              递归收集所有 ModMenuRow
│   └── LooksLikeModMenuRow():
│       ① 是 Control，Size 合理
│       ② 有子节点 "Title" / "Tickbox"
│       ③ 有 Mod/mod/_mod 属性（反射）
│       ④ type.Name 包含 "ModMenuRow"
├── CleanupOrphanIcons(screen, rows)     移除挂到消失行/不可见行的图标
│   └── CollectHideIcons() 递归 + IsRowLikelyVisible()
├── foreach (row in rows):
│   ├── IsRowLikelyVisible()?            检查 row 是否在 ModsBorder 范围内
│   ├── 已有 HideIcon? → RefreshLayout()
│   └── 无 → TryInjectIcon(row)
│       ├── ResolveModIdentity(row)
│       │   ├── ReadTitleText()          找 "Title" 子节点 → text 属性
│       │   └── ReadStableModId()         找 Mod.manifest.id (稳定 ID)
│       ├── IsAnyHidden(stableId, title)  检查是否已标记隐藏
│       ├── MigrateLegacyHiddenKey()      旧键迁移
│       ├── new HideIconNode()
│       ├── icon.ConfigureIcon(stableId, hidden)
│       ├── row.AddChild(icon)
│       └── icon.RefreshLayout()
└── 日志 injected={n} realigned={m}
```

### 2.3 ResolveModIdentity 键选择策略

```
优先级：
  1. ReadStableModId(row)     → Mod.manifest.id（游戏原生稳定标识）
  2. ReadTitleText(row)       → Title 子节点文本（显示名称）
  若两者都空 → null（该行不注入图标）
  
返回 ModIdentity(StableId, DisplayKey)
  → ConfigureIcon 使用 StableId 存库
  → IsAnyHidden 同时检查 StableId 和 DisplayKey
```

---

## 3. 大眼睛注入器：VanillaModeToggleInjector

### 3.1 生命周期

```
_Ready()
  → GD.Print("started (persistent mode)")

_Process(delta) 每 0.35s:
  → TryInject()
```

### 3.2 TryInject() 详细流程

```
TryInject()
├── FindModdingScreen(Root)            同小眼睛注入器逻辑
├── 已存在 VanillaModeToggle? → EnsureTogglePlacement()
└── 不存在 → 创建
    ├── Config.Load()                   刷新配置
    ├── new VanillaModeToggleNode()
    ├── btn.Configure(vanilla)
    ├── EnsureTogglePlacement(btn)      锚定左上角 (18,18)-(66,66)
    └── screen.AddChild(btn)
```

---

## 4. HideIconNode 状态机

```
                    ┌─────────────┐
                    │   _Ready()  │
                    │ 加载纹理    │
                    │ 更新视觉    │
                    │ 延迟重定位  │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
           ┌───────│  VISIBLE    │◄──────────┐
           │       │ IsHiddenState│           │
           │       │ = false     │           │
           │       └──────┬──────┘           │
           │              │                  │
           │     用户点击 (_GuiInput)         │
           │              │                  │
           │    ┌─────────▼──────────┐       │
           │    │ IsHiddenState =    │       │
           │    │ !IsHiddenState     │       │
           │    │ Config.ToggleHidden│       │
           │    │ Config.Save()      │       │
           │    │ PlayClickSound()   │       │
           │    └─────────┬──────────┘       │
           │              │                  │
           │    ┌─────────▼──────────┐       │
           └───►│  HIDDEN           │───────┘
                │ IsHiddenState     │  再次点击
                │ = true            │
                └──────────────────┘

_Process(delta) 持续运行:
  → 每 0.20s 执行 ApplyAnchorsAndOffsets()
    → 计算相对 row / tickbox / folder 的位置
    → 检查是否在 ModsBorder 范围内 → 设置 Visible
```

---

## 5. 过滤层：键匹配逻辑

### 5.1 联机条目的格式

游戏 `ModManager.GetGameplayRelevantModNameList()` 返回格式：
```
"ModId-1.0.0"
"SomeOtherMod-2.5.1-beta.3"
```

### 5.2 HiddenModIds 存储格式

UI 点击保存的是 `manifest.id`（不含版本）：
```
"ModId"
"SomeOtherMod"
```

### 5.3 ShouldStripFromMultiplayerList(entry) 匹配策略

```
输入: entry = "ModId-1.0.0"
  │
  ├── ① HiddenModIds.Contains("ModId-1.0.0")?  → 精确匹配（无版本后缀的 ID）
  │
  ├── ② TryExtractBaseModId("ModId-1.0.0") = "ModId"
  │     HiddenModIds.Contains("ModId")?          → 提取基础 ID 匹配
  │
  └── ③ 遍历 HiddenModIds 每个 hiddenId:
        entry.StartsWith("hiddenId-") 且后缀 LooksLikeVersionSuffix?
          → 前缀匹配（兼容未来格式变化）

结果: 任一命中 → true（从列表移除该条目）
```

---

## 6. 配置持久化

### 6.1 存储位置

```
%APPDATA%/SlayTheSpire2/ModListHider/hidden_mods.json
```

### 6.2 JSON 结构

```json
{
  "hidden_mods": ["ModId1", "ModId2"],
  "vanilla_mode": false,
  "debug_mode": false
}
```

### 6.3 生命周期

```
Load()
  → 文件存在 → 反序列化 hidden_mods + vanilla_mode + debug_mode
  → 文件不存在 → MergeNoClientCheatsConfig() 尝试从 NCC 迁移

Save()
  → 序列化全部字段 → 写入文件

ReloadFromDisk()
  → 读取文件
  → Intersect 保留当前会话新增条目
  → Union 合并文件新条目
  → 不改变 vanilla_mode / debug_mode

MigrateLegacyHiddenKey(oldKey, newKey)
  → Remove(oldKey) → Add(newKey)
  → 用于将旧的 Title 键迁移到稳定 manifest.id 键
```

---

## 7. 调试机制

| 触发方式 | 功能 |
|----------|------|
| Ctrl+Shift+F8 | 切换 DebugMode，持久化到 JSON |
| DebugMode=true | DebugLog 输出注入/布局/过滤详细日志 |
| DebugMode=true | ModMenuRowInjector 输出行布局 dump（前 12 次） |
| VanillaModeToggleInjector | 注入成功/失败自动写 `ModListHider_vanilla_debug.txt` |

---

## 8. 构建流程 (build.ps1)

```
1. dotnet build -c Debug     → .godot/mono/temp/bin/Debug/ModListHider.dll
2. Godot export pck          → build/ModListHider.pck
3. Copy DLL → mods/ModListHider/
4. Copy PCK → mods/ModListHider/
5. Copy mod_manifest.json → mods/ModListHider/ (re-serialized via Python)
```

部署目标：`K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\ModListHider\`