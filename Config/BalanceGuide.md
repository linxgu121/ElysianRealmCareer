# Balance Guide / 数值调整指南

Barotrauma 的普通 XML Mod 没有全局变量机制，游戏不会读取独立的“数值表”并自动替换到其他 XML。

因此本项目采用“真实生效文件 + 索引指南”的方式：玩家按照本文件定位到对应 XML，直接改游戏实际读取的数值。

## 快速入口

| 想调整的内容 | 修改文件 | 查找 identifier / 关键词 |
| --- | --- | --- |
| 真我职业初始技能、生命修正 | `Content/Jobs/Jobs.xml` | `Job identifier="realme"` |
| 天赋树结构、前置、分支 | `Content/Talents/TalentTrees.xml` | `TalentTree jobidentifier="realme"` |
| 天赋触发条件、触发间隔、直接伤害倍率 | `Content/Talents/RealmeTalents.xml` | 天赋 identifier |
| 大多数 buff 数值、持续时间、属性加成 | `Content/Afflictions/RealmeAfflictions.xml` | affliction identifier |
| 爱矛伤害、爆炸、合成成本 | `Content/Items/LoveSpear.xml` | `Item identifier="lovespears"` |
| 爱莉希雅服装价格、护甲、技能加成 | `Content/Items/ElysiaGear.xml` | `Item identifier="Elysiagear"` |
| 显示文本里的百分比描述 | `Localization/SimplifiedChinese.xml` | `talentdescription.*` |

## 数值规则

- `value="0.25"` 这类属性加成通常表示 `+25%`。
- `addeddamagemultiplier="1.5"` 表示额外 `+150%` 伤害倍率。
- `damagemultiplier="0.8"` 表示承受 `80%` 伤害，也就是约 `-20%` 伤害。
- `duration="60"` 通常表示持续 `60` 秒。
- `amount="10"` 是 affliction 强度，不一定等于秒数；是否持续取决于目标 affliction 的 `duration`、`maxstrength`、`strengthchange`。
- `strengthchange="-1"` 表示强度会衰减，改大或改小会影响 buff 留存时间。

## 常改 Buff

| 效果 | 文件 | identifier | 推荐改的属性 |
| --- | --- | --- | --- |
| 来自人律的祝福 | `RealmeAfflictions.xml` | `ablessingfromherrscherofhuman` | `RepairSpeed`、`RepairToolStructureRepairMultiplier` |
| 人律的本源 | `RealmeAfflictions.xml` | `asourcefromherrscherofhuman` | `RepairSpeed`、`RepairToolStructureRepairMultiplier` |
| 来自始源的祝福 | `RealmeAfflictions.xml` | `ablessingfromherrscheroforigin` | `MaximumHealthMultiplier`、`MedicalItemEffectivenessMultiplier`、`BuffItemApplyingMultiplier` |
| 始源的本源 | `RealmeAfflictions.xml` | `asourcefromherrscheroforigin` | `MaximumHealthMultiplier`、`MedicalItemEffectivenessMultiplier`、`BuffItemApplyingMultiplier` |
| 似乎得认真点啦哪 | `RealmeAfflictions.xml` | `itseemstohavetobeserious` | `duration`、`AttackMultiplier` |
| 以我为始，以我为终 | `RealmeAfflictions.xml` | `startwithmeandendwithme` | `duration`、`RepairToolStructureRepairMultiplier`、`SwimmingSpeed` |
| 梦幻的共演哦 | `RealmeAfflictions.xml` | `adreamycoperformance` | `AttackMultiplier` |
| 送你一点惊喜 | `RealmeAfflictions.xml` | `sendyoualittlesurprise` | `duration`、`RepairSpeed`、`RepairToolStructureRepairMultiplier`、`MeleeAttackSpeed` |
| 逃不掉哦 | `RealmeAfflictions.xml` | `youcannotescape` | `AttackMultiplier` |
| 打不着哦 | `RealmeAfflictions.xml` | `cannotfight` | `MovementSpeed` |

## 常改天赋触发

| 天赋 | 文件 | identifier | 推荐改的属性 |
| --- | --- | --- | --- |
| 沉睡的种 | `RealmeTalents.xml` | `DormantSeeds` | `RangedSpreadReduction value` |
| 水晶之种 | `RealmeTalents.xml` | `CrystallineSeed` | `MechanicalRepairSpeed value` |
| 至高的祝福 | `RealmeTalents.xml` | `BlessingofZenith` | `EngineSpeed value` |
| 初绽的祝福 | `RealmeTalents.xml` | `BlessingofFirstBloom` | `addeddamagemultiplier` |
| 至爱的祝福 | `RealmeTalents.xml` | `BlessingofLove` | `floodpercentage`、`amount` |
| 初醒的祝福 | `RealmeTalents.xml` | `BlessingofFirstAwakening` | `maxtriggercount`、`amount` |
| 至善的祝福 | `RealmeTalents.xml` | `BlessingofBenevolence` | `SkillGainSpeed value` |
| 天眷始终，爱佑世人 | `RealmeTalents.xml` | `EnlightenedSalvation` | `stun multiplier` |
| 风与月之誓 | `RealmeTalents.xml` | `AnOathtoWindandMoon` | `addeddamagemultiplier` |
| 光折斗转，明映星芒 | `RealmeTalents.xml` | `RisingStarbeams` | `damage multiplier` |
| 花与鸟之矢 | `RealmeTalents.xml` | `AnArrowforFlowerandFowl` | `HelmSkillBonus`、`MedicalSkillBonus` |
| 落花刹那，光翳明灭 | `RealmeTalents.xml` | `ShatteredTransience` | `MedicalSkillBonus`、`WeaponsSkillBonus` |

## 物品调整

### Elysiagear

文件：`Content/Items/ElysiaGear.xml`

- `Price baseprice`：商店基础价格。
- `Fabricate requiredtime`：制造时间。
- `Item identifier="aluminium" amount`：制造材料。
- `damagemultiplier`：护甲减伤倍率。
- `SkillModifier skillvalue`：穿戴时技能加成。

### lovespears

文件：`Content/Items/LoveSpear.xml`

- `maxstacksize`：堆叠上限。
- `Fabricate requiredtime`：制造时间。
- `RequiredSkill level`：制造技能需求。
- `Attack structuredamage`：直接命中结构伤害。
- `explosiondamage strength`：直接命中爆炸伤害。
- `Explosion range`：爆炸范围。
- `Explosion structuredamage`：爆炸结构伤害。
- `Explosion itemdamage`：爆炸物品伤害。
- `burn strength`、`stun strength`：爆炸附加状态。

## 改完后检查

运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\ValidateMod.ps1
```

这个脚本只能检查 XML 和资源路径，不能判断数值是否平衡。平衡仍需要进游戏测试。
