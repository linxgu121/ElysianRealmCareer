# Elysian Realm Career Core / 乐土刻印职业核心

## 当前加载内容

- `Localization/SimplifiedChinese.xml`: 简体中文文本。
- `Content/Jobs/Jobs.xml`: `realme` 真我职业。
- `Content/Talents/TalentTrees.xml`: `realme` 天赋树，不覆盖原版职业树。
- `Content/Talents/RealmeTalents.xml`: 真我天赋效果。
- `Content/Afflictions/RealmeAfflictions.xml`: 天赋 buff 和数值效果。
- `Content/Items/ElysiaGear.xml`: `Elysiagear` 真我职业服装。
- `Content/Items/ElysianItems.xml`: `pastflower` 往事的飞花、爱莉希雅的喇叭、圣痕槽位等物品。
- `Content/Items/LoveSpear.xml`: `lovespears` 爱矛物品。
- `Content/Recruitment/Factions.xml`: `ElysianRealm` / 逐火之蛾势力。
- `Content/Recruitment/LocationTypes.xml`: 往世乐土哨站地点类型，固定归属逐火之蛾，并复用原版 `City` 哨站模块池。
- `Content/Recruitment/NPCSets.xml`: 逐火之蛾哨站 NPC 与可招募 `hireableElysia`。
- `Content/Recruitment/OutpostGeneration.xml`: 按原版大城市站风格生成逐火之蛾哨站。

## 必需 LuaCs 客户端前置

- `Lua/Autorun/ElysianDebug.lua`: LuaCs 自动运行探针，用于确认 Lua 自动加载链路已经进入本 Mod。
- `CSharp/Client/ElysianPortraitPlugin.cs`: 客户端 C# 脚本插件，已作为 `Other` 文件随包发布；它会拦截 `CharacterInfo.DrawIcon(...)`，将 `realme` 职业的生成头像替换为指定图片。
- `CSharp/Client/ElysianGameplayPlugin.cs`: 客户端 C# 玩法脚本插件，负责往事的飞花蓄力射击/聚能粒子、爱莉希雅的喇叭范围鼓励/敌对 AI 嘲讽尝试、圣痕本源条件门控。
- 默认头像图片是 `Assets/UI/elysia_portrait.png`。要改成其他图片，修改脚本里的 `PortraitRelativePath`。
- 当前版本不使用 `ModConfig.xml`，依赖 LuaCs 对 `CSharp/Client` 与 `Lua/Autorun` 的旧式自动扫描；复制到游戏目录时请删除旧包残留的 `ModConfig.xml`。

安装前置：

- 客户端安装并启用 Client-Side LuaCs。
- LuaCs 中启用 C# 执行。
- 未安装 LuaCs 时，原版 Barotrauma 可能仍会读取 XML 内容，但不属于本 Mod 的支持安装方式。

## 往事的飞花弓机制

- 物品定义：`Content/Items/ElysianItems.xml` 中的 `pastflower`，弹药定义为 `Content/Items/LoveSpear.xml` 中的 `lovespears`。
- 持弓方式：双手持弓，右键进入蓄力判定；物品栏或弓的隐藏弹仓中必须有 `lovespears`。
- 普通射击：右键蓄力大于 0.5 秒且小于 15 秒时，左键会自动装填并发射 1 根爱矛。
- 超蓄力射击：右键蓄力达到 15 秒后，左键会删除物品栏中所有可用爱矛，并发射 1 根强化爱矛；伤害按消耗爱矛数量倍增，发射冲击量按正常值的 10 倍尝试覆盖。
- 爆炸效果：超蓄力爱矛命中后触发一次范围爆炸伤害，当前数值在 `CSharp/Client/ElysianGameplayPlugin.cs` 顶部常量中统一管理。
- 视觉反馈：蓄力时会在鼠标/准星附近和角色弓附近绘制粉色聚能粒子，粒子数量、大小和透明度随蓄力时间增加而增强。
- 关键日志：普通蓄力就绪会出现 `[ElysianRealm] Pastflower bow normal charge ready`；15 秒超蓄力就绪会出现 `[ElysianRealm] Pastflower bow super charge ready`；HUD 视觉链路首次绘制会出现 `[ElysianRealm] Pastflower charge visuals are drawing`。

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
- LuaCs 已启用 C# 执行，并能扫描 Mod 根目录下的 `CSharp/Client` 与 `Lua/Autorun`；确认游戏目录中没有旧版残留的 `ModConfig.xml`。
- LuaCs 日志中出现 `[ElysianRealm] Client portrait patch registered`、`[ElysianRealm] Gameplay plugin registered`、`[ElysianRealm] Portrait loaded` 和 `[ElysianRealm] Realme portrait overlay drawn`。
- 使用往事的飞花右键蓄力时，日志会出现 `[ElysianRealm] Pastflower charge visuals are drawing`；蓄力满 15 秒后会出现 `[ElysianRealm] Pastflower bow super charge ready`；随后左键射击会出现 `[ElysianRealm] Pastflower super shot prepared`。
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
