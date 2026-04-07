# 联机屏蔽 / MP Mod Hider + Vanilla Mode

**当前版本 v0.3.1**

> English documentation below.

---

## 功能说明

本 Mod 为 **杀戮尖塔 2（Slay the Spire 2）** 的联机模组管理界面提供两个增强功能：

### 1. 单个 Mod 显示/隐藏（眼睛图标）
在模组管理页面，每行 Mod 右侧有一个**眼睛图标**：
- **睁开（青色）**：联机时向对方显示此 Mod
- **闭上（红色）**：联机时在对方看到的 Mod 列表中**隐藏**此 Mod

状态会在进入模组管理页面时**自动恢复**（基于上次退出时的设置）。

### 2. 原版模式（大眼睛图标）
模组管理页面**左上角**有一个额外的**大眼睛图标**，控制**原版模式（Vanilla Mode）**：

| 状态 | 图标颜色 | 效果 |
|------|----------|------|
| 原版模式 **关闭** | 青色 / 睁眼 | 各 Mod 行右侧小眼睛控制该 Mod 是否在联机列表中显示（原有行为） |
| 原版模式 **开启** | 红色 / 闭眼 | 联机握手时向对方报告**没有任何 Mod**（欺骗服务端检测）；你仍可以正常使用所有本地 Mod（皮肤、界面增强等） |

> **何时使用：**
> - 若要与**未装任何 Mod 的原版玩家**联机，或**加入他们的房间**，请开启此模式。
> - 如果仅想对特定 Mod 隐藏，使用各行右侧的小眼睛图标即可，无需开启原版模式。

---

## 安装方式

1. 下载本 Release 的 `ModListHider-v0.3.1.zip`
2. 解压，将 `ModListHider.dll` 和 `mod_manifest.json` 放入：
   ```
   <你的SlayTheSpire2安装目录>\steamapps\common\Slay the Spire 2\mods\ModListHider\
   ```
3. 启动游戏，在模组管理页面确认 Mod 已启用

---

## 已知限制

- **本地 Mod 效果**：原版模式开启时，本地 Mod（如皮肤、界面增强）**仍然会在你的客户端正常运行**，只是联机时对方不会在你的列表中看到这些 Mod。
- **对方 Mod 不影响你**：原版模式只影响**你自己的** Mod 列表显示，不影响对方装了什么 Mod。
- 不支持在游戏中途切换原版模式，建议在**进入联机房间前**设置好，**联机过程中不要改动**。

---

## 文件结构

```
ModListHider/
├── mod_manifest.json    ← Mod 清单，游戏加载用
├── ModListHider.dll     ← 编译后的程序集
├── README.md            ← 本说明文件
└── toRelease/          ← 各版本 Release zip 发布包
    └── ModListHider-v0.3.1.zip
```

---

## 致谢

- 本 Mod 使用 [Lib.Harmony](https://github.com/pardeike/Harmony) 实现热补丁
- 杀戮尖塔 2 Mod 开发社区

---

---

# MP Mod Hider + Vanilla Mode (English)

**Current version v0.3.1**

---

## Features

### 1. Per-Mod Eye Icons
Each mod row in the modding screen has a small **eye icon** on the right:
- **Open (cyan)**: This mod is visible to multiplayer peers
- **Closed (red)**: This mod is hidden from the multiplayer mod list shown to others

Settings are **automatically restored** when you revisit the modding screen.

### 2. Vanilla Mode (Large Eye Icon)
A **large eye icon** in the **top-left corner** of the modding screen toggles **Vanilla Mode**:

| State | Icon | Effect |
|-------|------|--------|
| Vanilla Mode **OFF** | Cyan / Open | Each mod's small eye icon controls its visibility in MP lists (existing behavior) |
| Vanilla Mode **ON** | Red / Closed | MP handshake sends **no mods**; peers see you as an unmodded client. All local mods (skins, UI enhancements, etc.) still work for you |

> **When to use:**
> - Turn Vanilla Mode **ON** to join unmodded (vanilla) players or rooms.
> - If you only want to hide specific mods from specific players, use the per-mod eye icons instead — no need to enable Vanilla Mode.

---

## Installation

1. Download `ModListHider-v0.3.1.zip` from this release
2. Extract and place both files into:
   ```
   <YourSlayTheSpire2Path>\steamapps\common\Slay the Spire 2\mods\ModListHider\
   ```
   - `ModListHider.dll`
   - `mod_manifest.json`
3. Launch the game and verify the mod is enabled in the modding screen

---

## Known Limitations

- **Local mod effects**: When Vanilla Mode is ON, local mods (skins, UI tweaks, etc.) **still run normally on your client**; they are simply not reported to MP peers.
- **Peer mods unaffected**: Vanilla Mode only affects **your own** mod list, not what other players have installed.
- Do not toggle Vanilla Mode while already in a multiplayer session — configure it **before** entering the MP room.

---

## Technical Details

- Built with [Lib.Harmony](https://github.com/pardeike/Harmony) v2.3.3
- Targets .NET 9 / Godot.NET SDK 4.5.1
- `affects_gameplay: false` — this mod does not alter card values, combat mechanics, or run outcomes

---

## Changelog

### v0.3.1
- **Fix**: Android 端入口点问题 —— 改用 `[ModuleInitializer]` 确保 DLL 加载时自动初始化（之前 Android 上眼睛图标和 Vanilla Mode 注入失效）
- 保持与 PC 端行为一致

### v0.3.0
- **New**: Vanilla Mode — hide all mods from MP handshake to join vanilla players
- Large eye toggle in top-left of modding screen (cyan = OFF, red = ON)
- Hover tooltip in Chinese/English based on game language setting
- `hidden_mods.json` now persists `vanilla_mode` state
- Two patches: `InitialGameInfoFilterPatch` (vanilla join) + `ModListFilterPatch` (in-game MP list)

### v0.2.x
- Per-mod eye icons (hide individual mods from MP mod list)
- Settings auto-saved to `hidden_mods.json`

---

## Credits

- [Lib.Harmony](https://github.com/pardeike/Harmony)
- Slay the Spire 2 Modding Community
