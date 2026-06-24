# ModListHider — 隐藏功能在最新版游戏（0.107.1）下的真实生效矩阵

分析日期 2026-06-19 | 反编译档 `Tools/sts.dll历史存档/sts2_decompiled20260619/`

---

## 1. 结论先行

**当前 ModListHider 的隐藏功能在很多场景下确实"用不了"**，根因不是 patch 没生效，而是游戏在客机端有**三层串行检查**，ModListHider 只覆盖了第一层，后两层无法绕过。

| 检查层 | 数据来源 | 现有 patch 是否覆盖 |
|--------|----------|---------------------|
| ① `gameplayAffectingMods` 列表对比 | `ModManager.GetGameplayRelevantModNameList()` | ✅ `ModListFilterPatch` 已 patch |
| ② `idDatabaseHash` 对比 | `ModelIdSerializationCache.Hash`（启动时基于已加载 mod 计算） | ❌ **未覆盖** |
| ③ `otherMods` 列表对比 | `ModManager.GetNonGameplayRelevantModNameList()` | ❌ **未覆盖**（但只 Warn 不拒绝） |

第 ② 层是真正的拦路虎——只要本地装了**任何注册了 ModelDb 条目**的 mod，hash 就和原版/差异 mod 集的对端不一致，连接被直接拒绝。

---

## 2. 客机加入流程的完整代码路径

`MegaCrit.Sts2.Core.Multiplayer.Game.JoinFlow` 客机收到 `InitialGameInfoMessage` 后的检查顺序：

```
1. Version 比对          → 不一致 throw VersionMismatch
2. gameplayAffectingMods 比对：
     local  = ModManager.GetGameplayRelevantModNameList()     ← 被 ModListHider 过滤
     remote = initialMessage.gameplayAffectingMods             ← 被 ModListHider 过滤（host 侧）
     双向 Except 任一 > 0 → throw ModMismatch
3. idDatabaseHash 比对：
     if (initialMessage.idDatabaseHash != ModelIdSerializationCache.Hash)
         throw VersionMismatch                                 ← ModListHider 没碰这里
4. otherMods 比对：
     双向 Except → 仅 Warn，不抛异常                          ← 这是 ModListHider 真正能起作用的地方
```

`ModelIdSerializationCache.Init()` 的逻辑：

```
foreach mod in ModManager.Mods where state == Loaded:
    foreach AbstractModel 子类型 in mod.assembly:
        把 (Category, Entry) 写入 hash 流
Hash = xxHash32 over all ids
```

**关键点**：
- 不管 `affectsGameplay` 是 true 还是 false，只要 mod 加载且包含 `AbstractModel` 子类型，就会进 hash
- Hash 在游戏启动时一次性算完，多人握手时双方对比

---

## 3. 真实生效矩阵

| 场景 | 用户角色 | 隐藏的 mod 性质 | 结果 |
|------|---------|----------------|------|
| **A** | 主机 | `affects_gameplay=false`，无 ModelDb 注册（皮肤/UI/视觉） | ✅ **生效**：客机看不到这个 mod，能正常加入 |
| **B** | 主机 | `affects_gameplay=false`，但**注册了** AbstractModel 子类（少见） | ❌ Hash 不一致 → 客机端 VersionMismatch |
| **C** | 主机 | `affects_gameplay=true`（卡牌/遗物/角色/敌人） | ❌ Hash 不一致（必定注册 AbstractModel）→ 客机端 VersionMismatch |
| **D** | 客机 + Vanilla Mode | 装的全是 `affects_gameplay=false` 且无 ModelDb 注册的 mod | ✅ **生效**：能加入原版主机 |
| **E** | 客机 + Vanilla Mode | 任一 mod 注册了 AbstractModel 子类 | ❌ 本地 hash ≠ 原版 host hash → 进不去 |
| **F** | 客机 + 单 mod 隐藏 | `affects_gameplay=false`，无 ModelDb 注册 | ✅ **生效**：otherMods 不一致只 Warn |
| **G** | 客机 + 单 mod 隐藏 | `affects_gameplay=true` 或注册了 AbstractModel | ❌ Hash 不一致 → 进不去 |

**用户/粉丝最常踩的坑**：场景 C、E、G —— 想隐藏改卡/改遗物/改角色 mod，或用 Vanilla Mode 时本地装了这类 mod。

---

## 4. 为什么 ModelDb hash 这道坎绕不过

技术上有四种思路，全部都有严重副作用：

### 方案一：拦截 `ModelIdSerializationCache.Init()`，在 hash 计算前剔除隐藏 mod
- **风险**：hash 改了，但本地 mod 仍然加载，运行时一旦本地代码引用这些 mod 注册的 entry 做序列化（包括存档、联机同步任意一个 ModelId 字段），都会 `ArgumentException: ModelId entry XXX could not be mapped to any net ID!` 直接崩。
- **结论**：不可行。

### 方案二：反射改写 `ModelIdSerializationCache.Hash`，让它装作没装某个 mod
- 同方案一的副作用：本地仍然加载，但 hash 假装没加载。
- 任何对方发来的 ModelId 字段，本地能解析（因为本地真有这个 entry）；但本地发出的 ModelId，对方解不了 → 中途断连。
- **结论**：不可行。

### 方案三：吞掉 JoinFlow 的 `idDatabaseHash` 异常
- patch JoinFlow 的检查逻辑跳过 hash mismatch。
- 但 hash mismatch 本质表示双方 ModelDb 条目集合不同。一旦实际游戏里同步到了对方有但本地没有的 entry → 同样崩。
- **结论**：理论上可行但极不稳定，会变成"能进房间但开打就崩"。

### 方案四：让 mod 作者把 `affects_gameplay` 改 false
- 不解决 hash 问题（hash 不看 `affects_gameplay`）。
- **结论**：无效。

**真正的边界**：
> ModListHider 的隐藏功能本质上只能影响"显示给对方看的 mod 列表"，**不能改变实际兼容性**。当且仅当被隐藏 mod **没有注册任何 ModelDb 条目** 时，隐藏才能让对方真正"看不见且能联机"。

---

## 5. 哪些 mod 会注册 ModelDb 条目

判定依据：mod 的 assembly 中存在继承自 `MegaCrit.Sts2.Core.Models.AbstractModel` 的类型。

近似规则（**仅供初步判断，不绝对**）：
- 加新卡 / 新遗物 / 新角色 / 新敌人 / 新事件 → 几乎必定注册
- 仅做 UI 注入 / Patch 现有逻辑 / 数值修改 / 调试工具 → 大概率不注册
- 如 ModListHider、NoClientCheats、MultiplayerTools 自身 → 不注册
- 如 SpeedX、quickRestart2、ModConfig → 也不注册
- 如 Watcher（角色 mod）、Kayla 等 → 必定注册

---

## 6. 给粉丝的实用建议（也是当前 mod 真实能力的边界）

1. **想隐藏的 mod 是皮肤/UI 类**：直接用，能生效。
2. **想隐藏的 mod 是卡/遗物/角色类**：当前版本做不到，建议改用"在联机前禁用该 mod 然后重启游戏"的传统方式。
3. **想用 Vanilla Mode 加入原版好友**：前提是你装的所有 mod 都不注册 ModelDb 条目。如果装了 Watcher 这类角色 mod，必须先禁用并重启。
4. **诊断办法**：进入联机失败时看 `godot.log`，如果出现 `ModelDb hash mismatch` → 就是这种情况；如果是 `Mod mismatch` → 是 ModListHider 列表过滤没生效，需要查代码。

---

## 7. 推荐的代码改进方向（非破坏性）

### 7.1 让 mod 自身能告诉用户隐藏会失败

在小眼睛旁加一个状态指示：检查该 mod 的 assembly 是否包含 `AbstractModel` 子类型，如果有 → 在小眼睛上叠一个红色感叹号，hover tooltip 提示"该 mod 注册了游戏数据，无法仅靠隐藏在联机中绕过"。

实现复杂度：低（反射扫 mod.assembly），只在大屏首次显示时扫一次缓存。

### 7.2 同步覆盖 `GetNonGameplayRelevantModNameList`

当前 `ModListFilterPatch` 只 patch 了 `GetGameplayRelevantModNameList`。`InitialGameInfoMessage.Basic()` 同时调用两者填充 `gameplayAffectingMods` 和 `otherMods`。

如果用户隐藏的是 `affects_gameplay=false` 的 mod，且当前模式是单 mod 隐藏（非 Vanilla），实际过滤的是 `otherMods` 列表——但这个列表对应的方法没被 patch，所以**单 mod 隐藏对皮肤类 mod 在 host 侧也没真正过滤干净**（仅靠 `InitialGameInfoFilterPatch` 的反射递归兜底）。

修复：在 `ModListFilterPatch.TargetMethods()` 的 `preferred` 数组中加上 `GetNonGameplayRelevantModNameList`。

### 7.3 在 README 中明确边界

把第 3 节的生效矩阵直接写进 README，避免粉丝继续误用。

---

## 8. 下一步建议执行顺序

1. ✅ 立即可做：补 `GetNonGameplayRelevantModNameList` 到 `ModListFilterPatch` 的目标方法（修复 7.2 的小漏洞）
2. ✅ 立即可做：更新 README，明确边界
3. ⏳ 中期：实现 7.1 的"该 mod 不可隐藏"红点提示
4. ❌ 不建议：尝试绕过 ModelDb hash 检查（任何方案都会引入运行期崩溃风险）