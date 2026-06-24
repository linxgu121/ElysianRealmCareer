# Elysian Realm Career Core / 乐土刻印职业核心

这是从旧 Workshop 包 `2960303865` 迁移出的核心职业框架。

## 当前加载内容

- `Localization/SimplifiedChinese.xml`：简体中文文本。
- `Content/Jobs/Jobs.xml`：`realme` 职业。
- `Content/Talents/TalentTrees.xml`：只保留 `realme` 天赋树，不再覆盖原版职业树。
- `Content/Talents/RealmeTalents.xml`：真我天赋效果。
- `Content/Afflictions/RealmeAfflictions.xml`：天赋 buff 和数值效果。
- `Content/Items/ElysiaGear.xml`：`Elysiagear` 真我职业服装。
- `Content/Items/LoveSpear.xml`：`lovespears` 爱矛物品。

## 暂时下线内容

招募、派系、哨站、任务、随机事件暂时不加载，已归档到：

- `Deprecated/Recruitment`
- `Deprecated/OriginalSnapshot`

这部分旧包强依赖旧版地图/任务/NPC 配置，后续应作为独立模块重新适配。

## 迁移决策

- 移除了 `filelist.xml` 中的 `LocationTypes`、`Factions`、`NPCSets`、`Missions`、`OutpostConfig`、`RandomEvents`。
- 移除了旧 `TalentTrees.xml` 对原版职业天赋树的整包覆盖，只保留 `realme`。
- 将资源路径整理到 `Assets/UI`、`Assets/Audio`、`Assets/Items`。
- `realme` 起始装备已改回 `Elysiagear`，服装贴图来自 `Assets/Items/Elysia`。
- `filelist.xml` 的 `gameversion` 暂保留旧包值；确认本机 Barotrauma 版本后再更新，避免手填错误版本。

## 下一步验证

把整个 `ElysianRealmCareer` 文件夹复制到 Barotrauma 的 `LocalMods` 后，在游戏 Mod 菜单启用。优先检查：

- Mod 是否能被识别并启用。
- 新职业「真我」是否出现在职业列表。
- 天赋树 UI 是否显示图标和中文描述。
- `InfiniteHelix` 是否能解锁 `lovespears` 配方。
- 游戏日志是否报告旧版 `CharacterAbility`、`StatValue` 或 `AbilityCondition` 名称变更。

本地离线检查可以运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ValidateMod.ps1
```

## 数值调整

玩家想改数值时，优先看 `Config/BalanceGuide.md`。它列出了职业、天赋、buff、爱矛、服装各类数值对应的真实 XML 位置。
