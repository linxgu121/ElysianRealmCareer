# Elysian Realm Career Core / 乐土刻印职业核心

## 当前加载内容

- `Localization/SimplifiedChinese.xml`: 简体中文文本。
- `Content/Jobs/Jobs.xml`: `realme` 真我职业。
- `Content/Talents/TalentTrees.xml`: `realme` 天赋树，不覆盖原版职业树。
- `Content/Talents/RealmeTalents.xml`: 真我天赋效果。
- `Content/Afflictions/RealmeAfflictions.xml`: 天赋 buff 和数值效果。
- `Content/Items/ElysiaGear.xml`: `Elysiagear` 真我职业服装。
- `Content/Items/LoveSpear.xml`: `lovespears` 爱矛物品。
- `Content/Recruitment/Factions.xml`: `ElysianRealm` / 逐火之蛾势力。
- `Content/Recruitment/LocationTypes.xml`: 往世乐土哨站地点类型，固定归属逐火之蛾，并复用原版 `City` 哨站模块池。
- `Content/Recruitment/NPCSets.xml`: 逐火之蛾哨站 NPC 与可招募 `hireableElysia`。
- `Content/Recruitment/OutpostGeneration.xml`: 按原版大城市站风格生成逐火之蛾哨站。

## 必需 LuaCs 客户端前置

- `ModConfig.xml`: LuaCs 专用配置，已作为 `Other` 文件随包发布；LuaCs 会读取它加载客户端脚本。
- `CSharp/Client/ElysianPortraitPlugin.cs`: 客户端 C# 脚本插件，已作为 `Other` 文件随包发布；它会拦截 `CharacterInfo.DrawIcon(...)`，将 `realme` 职业的生成头像替换为指定图片。
- `CSharp/Client/ElysianGameplayPlugin.cs`: 客户端 C# 玩法脚本插件，负责往事的飞花 15 秒蓄力全弹强化、爱莉希雅的喇叭范围鼓励/敌对 AI 嘲讽尝试、圣痕本源条件门控。
- 默认头像图片是 `Assets/UI/elysia_portrait.png`。要改成其他图片，修改脚本里的 `PortraitRelativePath`。

安装前置：

- 客户端安装并启用 Client-Side LuaCs。
- LuaCs 中启用 C# 执行。
- 未安装 LuaCs 时，原版 Barotrauma 可能仍会读取 XML 内容，但不属于本 Mod 的支持安装方式。

## 仍然下线内容

任务、随机事件，以及旧包里未迁移的专属装备仍然不加载，归档位置：

- `Deprecated/Recruitment`
- `Deprecated/OriginalSnapshot`

这部分旧包强依赖旧版地图、任务和 NPC 配置，后续应继续作为独立模块重构。

## 迁移决策

- 已恢复 `Factions`、`LocationTypes`、`NPCSets`、`OutpostConfig` 四类招募基础内容。
- 仍然不加载 `Missions`、`RandomEvents`。
- 逐火之蛾作为主势力加入，`controlledoutpostpercentage="10"`，和木卫二联盟/分裂组织一样参与主势力前哨站控制权逻辑。
- 新地点 `ElysianRealm` 通过 `UseOutpostModulesOfLocationType="City"` 复用原版最大城市哨站模块池。
- `OutpostGeneration.xml` 只覆盖主前哨站类型，给 `outpost`、`miningoutpost`、`researchoutpost`、`militaryoutpost`、`city` 增补逐火之蛾管理/安保 NPC，避免随机归属逐火之蛾的原版站点缺少对应 NPC。
- 哨站命名来自 `Content/Recruitment/ElysianRealmLocationNames.txt`。
- 旧的 Kevin/Aponia/Eden/Vill-V 专属装备没有迁移，当前 NPC 先使用原版装备，避免废弃资源泄漏进活动内容。

## 逐火之蛾专属哨站 NPC 规则

- 哨站长/任务 NPC：爱莉希雅，使用 `Elysiagear`。
- 保安：凯文，可重复生成；当前沿用原版保安装备，等待 `Kevingear` 物品定义补齐后再替换衣服。
- 人员管理：伊甸，使用 `campaigninteractiontype="Crew"`。
- 商人：帕朵菲利斯，保留 `merchantcity`、`merchantarmory`、`merchantmedical`、`merchantengineering` 等原版商店 identifier，确保库存仍走默认商店配置。
- 工程师：维尔薇。
- 医生：阿波尼亚，使用 `campaigninteractiontype="MedicalClinic"`。
- 潜艇升级工程师：梅，使用 `campaigninteractiontype="Upgrade"`。
- 英桀招募位：当前只放入 1 个 `realme` 可招募位；招募需要逐火之蛾声望达到 20，其余站内补位使用逐火之蛾势力下的原版职业。
- 专属 `ElysianRealm` 哨站不生成小丑教派/画皮教派等附属势力模块和 NPC。

## 本地验证

把整个 `ElysianRealmCareer` 文件夹复制到 Barotrauma 的 `LocalMods` 后，在游戏 Mod 菜单启用。优先检查：

- 已安装并启用 Client-Side LuaCs。
- LuaCs 已启用 C# 执行，并能读取 Mod 根目录的 `ModConfig.xml`。
- LuaCs 日志中出现 `[ElysianRealm] Client portrait patch registered`、`[ElysianRealm] Gameplay plugin registered`、`[ElysianRealm] Portrait loaded` 和 `[ElysianRealm] Realme portrait overlay drawn`。
- 使用往事的飞花右键蓄力满 15 秒后，日志会出现 `[ElysianRealm] Pastflower bow super charge ready`；随后左键射击会出现 `[ElysianRealm] Pastflower super shot prepared`。
- 使用爱莉希雅的喇叭时，日志会出现 `[ElysianRealm] Horn used`，其中会统计本次鼓励和嘲讽尝试数量。
- Mod 是否能被识别并启用。
- 新职业“真我”是否出现在职业列表。
- 新战役地图中是否能生成“往世乐土”系列哨站。
- 逐火之蛾势力是否出现在势力界面。
- 逐火之蛾哨站内是否能看到爱莉希雅，以及能否招募 `realme` 船员。
- 游戏日志是否报告旧版 `CharacterAbility`、`StatValue`、`AbilityCondition`、NPC 物品或哨站模块标识符错误。

本地离线检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ValidateMod.ps1
```

## 数值调整

玩家想改数值时，优先看 `Config/BalanceGuide.md`。它列出了职业、天赋、buff、爱矛、服装各类数值对应的真实 XML 位置。
