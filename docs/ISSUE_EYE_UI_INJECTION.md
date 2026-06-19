# ModListHider — 眼睛 UI 注入失败/消失问题分析

版本 0.3.2 | 分析日期 2026-06-19 | 游戏版本 0.107.1

---

## 1. 问题描述

用户反馈小眼睛 UI 存在两种故障模式：
- **注入失败**：进入模组管理页面后，部分或全部行右侧没有小眼睛图标
- **玩了一阵后消失**：小眼睛图标正常显示一段时间后，在游戏流程中消失且不再恢复

---

## 2. 当前注入架构

```
ModuleInit.Initialize() [仅执行一次，DLL 加载时]
    │
    └── CallDeferred() × 3:
         ├── VanillaModeToggleInjector → AddChild 到 sceneTree.Root
         ├── ModMenuRowIconInjector  → AddChild 到 sceneTree.Root
         └── DebugHotkeyWatcher      → AddChild 到 sceneTree.Root

各 Injector._Process(delta):
    → 持续扫描 sceneTree —— 但前提是 injector 自身仍在树中
```

---

## 3. 根因分析

### 3.1 核心问题：注入器是"一次性创建"，不防销毁

```
ModuleInit.Initialize() 仅在 DLL 加载时执行一次。
─────────────────────────────────────────────────────────
场景：游戏从主菜单 → 进入游戏 → 回到主菜单 → 重新进入

每次场景转换时，sceneTree.Root 可能被重建：
  - Godot 场景切换会销毁旧 root 子树
  - 注入器作为 root 的子节点被一起销毁
  - ModuleInit.Initialize() 不会再次运行
  - 注入器永久丢失，眼睛图标不再出现
```

### 3.2 具体故障路径

```
时间线：
  T0: DLL 加载 → ModuleInit 创建 injector → 眼睛正常
  T1: 玩家在主菜单 → 模组页面 → 眼睛显示正常
  T2: 玩家进入联机房间
  T3: 游戏过程中 sceneTree 重组 → injector 被 Free
  T4: 玩家退出回到模组页面 → 没有 injector → 眼睛不出现
```

### 3.3 游戏版本 0.107.1 可能的额外因素

新版本 `NModdingScreen` 结构已有变化（基于 2026-06-19 反编译档对比 06-16 档）：

| 变化 | 影响 |
|------|------|
| `NModdingScreen` 新增 `NConfirmModLoadingPopup` 确认弹窗 | ModdingScreen 打开流程增加异步步骤 |
| `ModManager` 多处逻辑调整 | Mod 行创建时机可能后移 |
| Scene tree 重组模式可能有变化 | Injector 更快被清理 |

这些变化可能导致：
- Injector 扫描开始时间点早于 ModdingScreen 可用时间点
- 首次扫描时 screen 为 null → 需要等待下次扫描
- 若 injector 在 screen 准备好之前被清理，永远无法注入

---

## 4. 代码级证据

### 4.1 一次性创建

```csharp
// src/ModuleInit.cs: 注入器只在 DLL 加载时创建一次
Callable.From(() =>
{
    var sceneTree = Engine.GetMainLoop() as SceneTree;
    if (sceneTree != null)
    {
        // 检查同名节点已存在 → 跳过（但不会检查是否被 Free 后需重建）
        if (sceneTree.Root.FindChild(RowInjectorNodeName, true, false) != null)
        {
            GD.Print("already exists, skipping.");
            return;
        }
        // 创建并添加
        var injector = new UI.ModMenuRowIconInjector { Name = RowInjectorNodeName };
        sceneTree.Root.AddChild(injector);
    }
}).CallDeferred();
```

### 4.2 无恢复机制

- 没有监听 `SceneTree.tree_changed` 信号
- 没有在 `_Process` 中自检 `IsInsideTree()`
- 没有从 Harmony patch 触发重新创建
- `FindChild` 使用 `owned: false` 可能遗漏某些 ownership 状态的节点

---

## 5. 修复方案

### 方案 A：自愈式注入器（推荐）

注入器在 `_Process()` 中自检存活状态，一旦发现不在树中则自动重建。

**改动点**：

1. `ModuleInit.Initialize()` 中改为注册一个**静态工厂回调**，不直接创建节点
2. `Injector._Process()` 开头检查 `IsInsideTree()`：
   - 若不在树中 → 获取当前 SceneTree → 重新 AddChild 自身
3. 或者：在 `ModuleInit` 中挂一个全局 `_Process` 看门狗，周期性检查 injector 是否存在

```pseudo
// 看门狗方案
class InjectorWatchdog : Node {
    public override void _Process(double delta) {
        var root = GetTree()?.Root;
        if (root?.FindChild("ModListHider_RowInjector", true, false) == null) {
            var injector = new ModMenuRowIconInjector { Name = "..." };
            root.AddChild(injector);
        }
        // 同样检查其他 injector
    }
}
```

### 方案 B：Harmony Patch 触发注入

不依赖持久化 Node 扫描，改为 Patch `NModdingScreen` 的生命周期方法。

```
[HarmonyPatch(typeof(NModdingScreen), "_Ready")]
Postfix:
  → 直接在 screen._Ready 时注入图标
  → 不需要持续扫描
  → 每次 screen 重建都会触发
```

**优点**：彻底消除"注入器被销毁"问题，注入与 screen 生命周期绑定
**缺点**：依赖具体的游戏类型名，每次游戏更新可能需要调整

### 方案 C：混合方案

保留当前持久化扫描作为**兜底**，同时增加 A 或 B 作为**主要注入路径**。

---

## 6. 额外诊断建议

### 6.1 增加存活日志

```csharp
// 在注入器 _Process 中
if (!IsInsideTree()) {
    GD.PrintErr("[ModListHider] CRITICAL: Injector not in tree! Attempting recovery...");
    TryRecover();
}
```

### 6.2 增加 sceneTree 变更监听

```csharp
GetTree().TreeChanged += () => {
    DebugLog.Info($"TreeChanged: injector in tree? {IsInsideTree()}");
};
```

### 6.3 检查 0.107.1 的 FindChild 行为

确认 `owned: false` 参数是否需要改为 `owned: true`。在 Godot 中：
- `owned: true` → 只搜索由同一 owner 拥有的子节点
- `owned: false` → 搜索所有子节点（通常是我们需要的）

---

## 7. 历史相关记录

| 日期 | 问题 | 状态 |
|------|------|------|
| 2026-04-13 | 小眼睛键匹配不一致（Title vs manifest.id） | 已在 v0.3.2 修复（改用 ReadStableModId） |
| 2026-04-13 | 小眼睛屏蔽效果弱于大眼睛（键不匹配导致过滤失败） | 同上 |
| 2026-04-13 | Android 端入口点失效 | 已在 v0.3.1 修复（改用 [ModuleInitializer]） |
| 2026-06-19 | 眼睛 UI 注入失败 / 玩一阵后消失 | **本文档分析对象** |

---

## 8. 推荐执行顺序

1. **首先**：启用 DebugMode（Ctrl+Shift+F8），复现问题，收集 godot.log
2. **验证假设**：确认 injector 是否在故障时仍存在于 sceneTree 中
3. **实现方案 A（自愈看门狗）**：改动最小，风险最低
4. **若仍不稳定**：叠加方案 B（Patch NModdingScreen._Ready）
5. **回归测试**：确认 0.107.1 下两套注入路径均正常工作