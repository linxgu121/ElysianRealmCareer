# Elysian Realm Career Core / 乐土刻印职业核心

## 当前加载内容

- `Localization/SimplifiedChinese.xml`: 简体中文文本。
- `Content/Jobs/Jobs.xml`: `realme` 真我职业。
- `Content/Talents/TalentTrees.xml`: `realme` 天赋树，不覆盖原版职业树。
- `Content/Talents/RealmeTalents.xml`: 真我天赋效果。
- `Content/Afflictions/RealmeAfflictions.xml`: 天赋 buff 和数值效果。
- `Config/ElysianBuffRules.xml`: LuaCs Buff 系统规则，当前用于圣痕槽位映射、人律/始源祝福标记映射和号角范围效果。
- `Content/Items/ElysiaGear.xml`: `Elysiagear` 真我职业服装。
- `Content/Items/ElysianItems.xml`: `pastflower` 往事的飞花、爱莉希雅的喇叭、圣痕槽位等物品。
- `Content/Items/LoveSpear.xml`: `lovespears` 爱矛物品。
- `Content/Recruitment/Factions.xml`: `ElysianRealm` / 逐火之蛾势力。
- `Content/Recruitment/LocationTypes.xml`: 往世乐土哨站地点类型，固定归属逐火之蛾，并复用原版 `City` 哨站模块池。
- `Content/Recruitment/NPCSets.xml`: 逐火之蛾哨站 NPC 与可招募 `hireableElysia`。
- `Content/Recruitment/OutpostGeneration.xml`: 按原版大城市站风格生成逐火之蛾哨站。

## 必需 LuaCs 客户端前置

- `Lua/Autorun/ElysianDebug.lua`: LuaCs 自动运行探针，用于确认 Lua 自动加载链路已经进入本 Mod。
- `CSharp/Client/ElysianBuffSystem.cs`: 客户端 Buff 框架，按状态、条件、触发器、数据黑板、仲裁器、效果拆分，当前管理圣痕槽位 Buff、人律/始源祝福 Buff 与号角范围效果。
- `CSharp/Client/ElysianPortraitPlugin.cs`: 客户端 C# 脚本插件，已作为 `Other` 文件随包发布；它会拦截 `CharacterInfo.DrawIcon(...)`，将 `realme` 职业的生成头像替换为指定图片。
- `CSharp/Client/ElysianGameplayPlugin.cs`: 客户端 C# 玩法脚本插件，负责往事的飞花蓄力射击/聚能粒子、爱莉希雅的喇叭范围鼓励/敌对 AI 嘲讽尝试，并把角色更新入口转交给 Buff 框架。
- 默认头像图片是 `Assets/UI/elysia_portrait.png`。要改成其他图片，修改脚本里的 `PortraitRelativePath`。
- 当前版本不使用 `ModConfig.xml`，依赖 LuaCs 对 `CSharp/Client` 与 `Lua/Autorun` 的旧式自动扫描；复制到游戏目录时请删除旧包残留的 `ModConfig.xml`。

安装前置：

- 客户端安装并启用 Client-Side LuaCs。
- LuaCs 中启用 C# 执行。
- 未安装 LuaCs 时，原版 Barotrauma 可能仍会读取 XML 内容，但不属于本 Mod 的支持安装方式。

## LuaCs Buff 框架

- 圣痕槽位 Buff 由 `CSharp/Client/ElysianBuffSystem.cs` 管理，旧的圣痕直连逻辑已经移除，避免重复刷新和卸下后残留。
- 可调规则在 `Config/ElysianBuffRules.xml`：`slot="0"` 是上位，`slot="1"` 是中位，`slot="2"` 是下位；`item` 是圣痕物品，`effect` 是槽位专属 Affliction，`strength` 是刷新强度。
- 槽位专属 Affliction 位于 `Content/Afflictions/RealmeAfflictions.xml`，identifier 以 `elysian_slot_stigmata_` 开头。旧的 `elysiastigmata_*_effect` 仍保留给原版基因拼接器兼容。
- 圣痕槽位物品定义在 `Content/Items/ElysianItems.xml` 的 `stigmataslot`，三个 `SubContainer` 分别限制上/中/下位圣痕，同时仍允许原版 `geneticmaterial`。
- 人律/始源祝福现在也走 Buff 框架：`ablessingfromherrscherofhuman`、`asourcefromherrscherofhuman`、`ablessingfromherrscheroforigin`、`asourcefromherrscheroforigin` 只作为隐藏标记；正式数值效果使用 `elysian_talent_*_effect`。
- 爱莉希雅的喇叭也走 Buff 框架：`HornRules` 控制物品 identifier、冷却、范围、友方鼓励和敌方嘲讽失败时的降级效果；物品 XML 只保留声音和持握定义。
- LuaCs 控制台应出现 `[ElysianRealm] Buff engine initialized`、`[ElysianRealm] Stigmata buff rules loaded`、`[ElysianRealm] Talent affliction buff rules loaded` 和 `[ElysianRealm] Horn buff rules loaded`。如果错槽，日志会出现 `Buff engine ignored ... expected slot ...`。

## 往事的飞花弓机制

- 物品定义：`Content/Items/ElysianItems.xml` 中的 `pastflower`，弹药定义为 `Content/Items/LoveSpear.xml` 中的 `lovespears`；`lovespears_super` 是 15 秒超蓄力时由脚本临时装入弓内的隐藏爆炸弹体。
- 持弓方式：双手持弓，右键进入蓄力判定；物品栏或弓的隐藏弹仓中必须有 `lovespears`。
- 普通射击：右键蓄力大于 0.5 秒且小于 15 秒时，左键会自动装填并发射 1 根爱矛。
- 超蓄力射击：右键蓄力达到 15 秒后，左键会删除物品栏中所有可用爱矛，并发射 1 根强化爱矛；伤害按消耗爱矛数量倍增，发射冲击量按正常值的 10 倍尝试覆盖。
- 爆炸效果：超蓄力爱矛命中后由 `lovespears_super` 的 `StatusEffect type="OnImpact"` 触发原生 `Explosion`，爆炸范围、结构伤害、物品伤害、燃烧和眩晕数值在 `Content/Items/LoveSpear.xml` 中统一管理。
- 视觉反馈：右键蓄力达到 0.5 秒后开始显示粉色 `gravityspherefx`/聚能视觉，能量会向持弓手附近汇聚；蓄力越久，核心特效越大，光线越强。
- 超蓄力表现：只有达到 15 秒并射击时才绘制弹道方向的激光；未达到 15 秒时不会出现激光。命中后会在命中位置显示 `Assets/UI/真我1.png` 爆炸图标。
- 语音反馈：15 秒超蓄力射击后，会在 `爱莉希雅-逃不掉哦~.ogg`、`爱莉希雅-送你一朵花~.ogg`、`人之律者-送你一点惊喜。.ogg` 中随机播放一条。
- 关键日志：普通蓄力就绪会出现 `[ElysianRealm] Pastflower bow normal charge ready`；15 秒超蓄力就绪会出现 `[ElysianRealm] Pastflower bow super charge ready`；HUD 视觉链路首次绘制会出现 `[ElysianRealm] Pastflower charge visuals are drawing`。

## 贴图与握持点调整

给其他玩家改图时，优先改 `Content/Items` 下的 XML，不需要改 C#：

- 弓 `pastflower`：`Content/Items/ElysianItems.xml`，找到 `Item identifier="pastflower"`。
- 爱矛 `lovespears`：`Content/Items/LoveSpear.xml`，找到 `Item identifier="lovespears"`。
- 爱莉希雅喇叭 `elysiahorn`：`Content/Items/ElysianItems.xml`，找到 `Item identifier="elysiahorn"`。
- 真我服装 `Elysiagear`：`Content/Items/ElysiaGear.xml`，找到 `Item identifier="Elysiagear"`。

常用字段：

- 物品栏图标：`InventoryIcon texture`、`InventoryIcon sourcerect`、`InventoryIcon origin`。
- 世界贴图：`Sprite texture`、`Sprite sourcerect`、`Sprite origin`、`Sprite depth`。
- 整体显示大小：物品本体上的 `scale`。
- 碰撞体大小：`Body width`、`Body height`。
- 手持点位：`Holdable handle1`、`Holdable handle2`、`Holdable aimpos`、`Holdable holdpos`、`Holdable holdangle`。
- 弓/枪类发射点：`RangedWeapon barrelpos`，往事的飞花当前是 `70,0`。
- 服装穿戴贴图：`Wearable` 节点里的各个小写 `sprite`，按 `name`/`limb` 区分，例如 `Head`、`Torso`、`RightArm`、`LeftLeg`；常改 `texture`、`sourcerect`、`origin`。

坐标和裁剪格式：

- `texture` 建议写 `%ModDir%/Assets/Items/文件名.png`，这样复制到其他人的本地目录也能工作。
- `sourcerect` 是 `x,y,width,height`，表示从贴图哪一块裁剪，例如 `0,0,1500,1500`。
- `origin` 通常是 `0.5,0.5`，表示贴图中心点；改服装肢体贴图时这个值会影响肢体对齐。
- `handle1`、`handle2`、`aimpos`、`holdpos`、`barrelpos` 都是 `x,y` 坐标。`x` 往右为正，`y` 往下为正；如果手拿得太靠左，就增大 `handle` 的 x；如果发射点不在弓尖，就调 `barrelpos`。
- 每次改完后重进游戏或重新加载 Mod 测试，手感调点位通常需要多次微调。

不会手改 XML 的玩家可以用小工具：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ItemVisualTuner.ps1
```

运行后按提示选择物品，直接回车表示保留原值。工具会在保存前自动生成 `.bak-日期时间` 备份。

也可以用命令一次性改某个物品，例如把往事的飞花的贴图、握持点和发射点改成新值：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ItemVisualTuner.ps1 -Identifier pastflower -SpriteTexture "%ModDir%/Assets/Items/MyBow.png" -IconTexture "%ModDir%/Assets/Items/MyBowIcon.png" -Handle1 "-35,0" -Handle2 "20,0" -AimPos "50,0" -BarrelPos "70,0"
```

改服装肢体贴图示例：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ItemVisualTuner.ps1 -Identifier Elysiagear -WearableSpriteName Head -WearableTexture "%ModDir%/Assets/Items/Elysia/NewHead.png" -WearableSourceRect "0,0,205,473" -WearableOrigin "0.79,0.18"
```

工具只负责改 XML 字段，不会自动处理图片尺寸。图片路径、文件名、大小写必须和 `Assets` 里的真实文件一致。

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
- LuaCs 日志中出现 `[ElysianRealm] Client portrait patch registered`、`[ElysianRealm] Gameplay plugin registered`、`[ElysianRealm] Buff engine initialized`、`[ElysianRealm] Stigmata buff rules loaded`、`[ElysianRealm] Talent affliction buff rules loaded`、`[ElysianRealm] Horn buff rules loaded`、`[ElysianRealm] Portrait loaded` 和 `[ElysianRealm] Realme portrait overlay drawn`。
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
