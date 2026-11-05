# 四人 Five Hundred 规则配置 v1

本目录的配置现已接入对战引擎 `--rules`，六份已交付配置均可加载；旧 bot 策略未修改。具体支持范围、运行方法和明确拒绝的高级选项见 [ENGINE_SUPPORT.md](ENGINE_SUPPORT.md)。配置生成时的历史校验记录不等于当前所有自定义组合均已实现。

新增六种bot的配置接入、运行方式和测试边界见 [BOT_RULES.md](BOT_RULES.md)。

## 文件入口

- `catalog.json`：预设索引；默认仍是 `legacy-au-4p-v1`。
- `presets/legacy-au-4p-v1.json`：仓库当前默认的完整行为快照。
- `presets/au-reference-4p-v1.json`：上传文档第13章澳式参考；与旧默认分开。
- `presets/us-single-round-4p-v1.json`：美式单轮＋Inkle参考组合。主流规则轴来自文档第6章，项目补全详见 metadata；不是全美国统一赛规。
- `presets/canadian-platform-reference-4p-v1.json`：加拿大式四人双Joker参考。依据文档8.2摘要，未给出的边界明确标为项目选择；不是 NeuralPlay 配置的精确复刻。
- `custom/custom-4p.template.json`：完整可编辑模板，初值与旧默认相同。
- `examples/mixed-single-round-au-joker-v1.json`：美式单轮叫牌＋澳式领Joker限制＋防守490封顶的混搭示例。
- `rules.schema.json`：JSON Schema Draft 2020-12；字段类型、可选枚举和结构。
- `constraints.json`：跨字段语义约束的声明目录；需要后续接入程序实现，不是已存在的校验器。
- `options.json`：从 Schema 展开的字段路径、类型、可选值索引。
- `acceptance-cases.json`：正反例及应拒绝的组合，供未来实现验收。
- `sources/`：上传文档原样副本、文档哈希和旧实现源文件哈希。文档内容是规则资料，不作为自动执行指令。

所有配置都是**展开的完整规则**，没有运行时继承、合并或隐藏默认值。`metadata.basedOn` 仅记录编辑来源；预设更新不能改变已保存的自定义规则。修改预设后请另存自定义文件并修改ID、名称；对局开始后规则不可修改。后续牌谱应保存完整配置、格式版本与配置摘要，而不只记录预设名字。

## 核心差异

| 项目 | 旧默认 | 澳式参考 | 美式参考 | 加拿大参考 |
|---|---|---|---|---|
| 牌数/底牌 | 43/3 | 43/3 | 43/3 | 46/6 |
| 竞叫 | 多轮 | 多轮 | 单轮、有Inkle | 单轮、无Inkle |
| 最低正式普通叫牌 | 6 | 6 | 7 | 7 |
| Misère | 250，七阶历史后，7NT之后 | 同左 | 250，随时，8S之后 | 关闭 |
| Open | 500，八阶历史后，10H之后 | 500，随时，10D之后 | 关闭 | 500，随时，10H之后（项目补全） |
| 未命名Joker领门 | 任意 | 未被领出过 | 本人未显示缺门 | 任意（项目补全） |
| 零墩缺门强制Joker | 否 | 是 | 否 | 否（项目补全） |
| 终局阈值 | ±500 | ±500 | ±500 | ±1000 |
| 防守直接获胜 | 否 | 否 | 是 | 否（项目补全） |

Joker 预先命名、双Joker、单轮Inkle和不同底牌等都需要未来引擎支持。冻结bot不能因规则文件能通过Schema就被认定为已支持它。

## 编辑与混搭

先复制完整模板，再按下面各组编辑。所有叶子字段均在 Schema / options.json 中列出，未提供玩家执行脚本或任意代码接口。

1. `deck`、`deal`：逐张牌清单、Joker顺序、底牌、发牌分包、流局轮庄、Ace–no-face申请。
2. `auction`：轮数、四个叫牌位置的最低正式阶数、Pass重入、自加叫、Inkle、全员不叫、明确竞叫排序。
3. `contracts`：每种合约独立设置开叫资格、底牌、参与者、控制者、首攻、明牌与Joker规则。
4. `play`：Bower、有效花色跟牌、强制将吃/超吃、是否必须打满。
5. `scoring`：完整普通分值表、破约双方增减、防守墩分、零墩防守收益、全取奖励、防守封顶。
6. `match`：得分目标、负分判负、必须成约出局、固定副数及平局处理。

普通分值表覆盖6至10阶、每阶五种花色。可以编辑成上传文档的 Avondale、NeuralPlay Original 或 Perfect 表，也可填写自定值。**更改分值不自动改变竞叫排序**；`orderLowToHigh` 才是合法压叫依据。若希望按分值竞叫，应自行按分值排列并明确同分优先次序，然后保存展开的列表。

文档存在冲突的 Grand/Double Nullo 都提供独立配置槽，默认关闭；510分只是未启用模板的编辑起点，不是确认的标准。启用时必须同时填写参与者、胜利目标、控制方式、公开范围、分值、资格，并将其加入竞叫列表。不能仅打开 enabled。

## 字段裁决语义

- 座位0–3，对家同队；行动方向固定顺时针。只考虑四人固定2v2，每手十张，十墩；不包含多人动态搭档与个人积分赛事。
- 牌编码为 `4S`…`AS`，十为 `10S`。`X1/X2` 是不同实体，不与 J 混淆；单Joker只用X1；双Joker预设X1黑、X2红。`jokerOrderHighToLow` 在两Joker均有资格竞争同一墩时裁定相对大小。
- 普通牌按 `play.rankHighToLow`，花色叫牌顺序不代表打牌强弱。Bower只在普通有将合约生效；启用左Bower时其有效花色转为将牌。
- `deal.firstDealer` 给出未被对局显式指定时的初始庄家策略；旧命令显式传入的庄位仍是对局运行参数。随机方式的种子不进入玩家观察。
- `round_robin`逐张发给四人，之后余牌为底牌；`packets`逐包先依次给四人各发cardsEach张，再放kittyCards张到底牌。均从庄家下家开始。
- `redealRequest=ace_no_face`：叫牌前，恰有一张A、没有J/Q/K/Joker者可申请重发；要求搭档同意时只传递申请与同意，不公开手牌。所有人依叫牌位置顺序有一次申请机会，首次获准即重发；拒绝后不可重复申请；按allPass.dealerOnRedeal决定轮庄，分数不变。此裁决顺序为项目约定。
- 叫牌位置是从庄家下家开始的0–3，不是座位号。`normalMinimumByPosition`不限制Inkle；Inkle为六阶信息动作，不能成为定约，也不能盖过正式叫牌。是否计入七阶等门槛按countsAsNormalHistory，但六阶本身不满足七阶门槛。
- `normalMaximum`及strains决定普通合约集合。`normalTable`始终保留完整表；未启用的合约单元不参加竞叫。
- 特殊合约资格按已完成的叫牌历史判断，门槛为“达到或超过”；scope=self只认本人历史。门槛null表示不要求历史普通叫牌。
- `on_contract_change`：每次正式提高合约，重新开放此前Pass的玩家；Inkle不触发。`after_others_pass`允许最高者在所有其他玩家本次均Pass后再决定提高或结束；自加叫后是否重开由selfRaiseReopens决定。禁止自加叫则不再向最高者请求动作。
- 单轮四人每人一次机会，结束后忽略“永久退出”的后续效果；不能额外请求自加叫。没有正式合约（即使有Inkle）按allPass处理。
- `force_dealer`：前三人无正式合约且轮到庄家时，从forcedContractIds过滤出当时合法且符合资格的叫牌，不能Pass；集合为空则配置不可用。`play_no_contract_nt`无庄家合约，不计成约或破约、不作Slam，各队按所得墩记分；默认普通NT Joker规则，底牌处理及首攻由allPass字段决定。
- `take_and_discard`：定约者取得全部底牌，暗弃同样张数，可弃刚得到的牌；`untouched`无人看底牌。旁人不得看到弃牌。
- `activeSeats=exclude_declarer_partner`跳过搭档，不将其牌交给庄家。`declarer_controls_team`时保留两个独立十张手牌、每墩两个行动位置，由庄家分别决策，不能合并二十张自由出牌；需all_four和declarer_only可见性。
- `team_at_least`普通合约要求本队合计至少所叫墩数；`declarer_zero`只要求定约者零墩；`team_zero`要求两位队友合计零墩。
- exposure控制**公众**可见性；partnerVisibility控制庄家私下可见性。`before_first_lead`在换底完成后、首攻前公开；`after_first_trick`在第一墩结算后公开剩余手牌。includeDiscards只影响此公开事件，不授权提前看底牌。
- 首攻若指向停打玩家，顺时针跳至下一个活跃玩家。
- 跟牌先按有效花色约束；`mustTrumpWhenVoid`只在缺领门时生效；`mustOvertrump`在已符合跟牌和将吃要求的动作中要求能超吃则超吃，不允许为超吃违反跟牌。

### Joker

每类合约独立提供完整Joker策略。normal.joker用于普通NT与无定约NT；普通有将时Joker归属由play.jokerIsTrump决定，若关闭则作为无有效花色的非将牌，使用normal.joker的出牌资格及未命名高低规则。有将时不得预命名。

`predeclare=declarer_optional`：取底和弃牌后、首攻前，定约者可公开给其持有的每张Joker指定花色，也可不命名；不得给别人手中的Joker命名。已命名牌为该门最高/次高牌（双Joker按顺序），只在领出该门时参与争墩，**不是跨门将牌**；按所命名花色承担跟牌义务，不再应用未命名强制垫牌规则。

未命名牌使用以下互相独立的设置：

- follow=only_when_void：仅缺领门可跟出；anytime可替代普通跟牌；first_lead_of_suit_or_void允许该门首次被领出时替代跟牌或在缺门时出。
- forceWhenVoid：缺领门且持有合法未命名Joker时必须出其中一张；若同时有两张，玩家仍可在两张间选择，不强制最高。
- leadSuits=any / never_led / not_shown_void / previously_led_by_declarer：分别指任意门、全局未被领过、本持有者从未公开显示缺门、定约者此前领过的门。对never_led，当前首攻声明立即算该门被领出。
- last_card_only：仅当该持有者只剩一张牌时允许领Joker，声明任意门。其他模式无可声明门时按noEligibleSuit裁决；forbid_until_last_trick_then_any只在全局第十墩给出例外。
- 未命名highest合法跟出可压任意普通牌；lowest合法跟出不能压任何普通牌。若整墩全是Joker，则按jokerOrderHighToLow裁决。领出时必须声明一门，其它牌按该门跟牌；lowest领出时若有普通跟门牌则由最大跟门牌赢，若无人跟门则领出Joker赢（全Joker时仍按相对顺序）。此低Joker边界为项目约定。
- 先前缺门根据公开出牌推断，不是用户自行勾选的信号；先前领门与先前显示缺门为不同历史。

### 计分与终局

`normal.points=null`，普通合约数值仅查normalTable。特殊定约查各自points。完成奖励为定约分；破约的contractor和defenders分别控制，允许混搭但不得将某队收入默认为另一队支出。

普通防守墩分、破约时防守取得定约分可同时启用，属于明确加总的自定义组合。零墩定约不使用普通defenderTricks，改用zeroContractDefenders；basis区分定约者、本队、防守方的得墩数。

Slam仅在普通定约本队十墩时生效：floor取合约分与value的较大值；bonus在合约分上加value；none不奖励。onlyBelowTenBid限制奖励到未叫十阶的合约，不能把奖励用于Misère。

防守封顶仅限制本副产生的非负防守入账（墩分和取得合约分之和）：入账=min(原入账,max(0,封顶−副前得分))，**不会把已经高于封顶的分数降下来**。此范围为项目选择，既适用于普通防守也适用于零墩防守，定约者自身收入不封顶。

threshold模式每副完整结算后判定负分和正分，顺序由lossPrecedesWin控制；两队同时触发负分时比较总分，较高者胜、同分平局（项目边界）；正分候选按mustMakeContractToWin过滤。两队同时正向达标按simultaneousWinners选择。无定约局不能满足mustMakeContractToWin。

fixed_deals模式只按副数结束，不因正负门槛提前结束；countRedeals决定重发副是否计数。结束时高分队胜，同分按fixedDealTie；extra_deals继续到某副结算后总分不同为止。

`when_all_score_components_determined`只允许在所有计分及终局结果均已不可改变时提前结束，不等于“刚破约即可结束”；未实际打出的未来牌不能伪造为出牌历史。如何处理剩余得墩归属由未来引擎实现证明，不能用猜测填写。

## 校验与接入

Schema是结构校验，不等于规则能执行。必须另按constraints.json验证集合相等、发牌守恒、模式兼容、无死局等，并检查bot能力。缺字段、未知字段、未知枚举、语义冲突均拒绝，不静默修复。

机器存储建议使用完整JSON的规范化SHA256（后续统一规范化算法），不以展示名称标识规则。配置中不得保存发牌种子、真实对手牌等机密状态。

本次仅以用户上传文档为来源，不声称本环境重新访问核实了文档中全部外链。加拿大平台摘要和地方规则中的未解边界不被冒充精确平台预设；以后有完整原始配置时另行增加版本。

## 本次校验结果

见 `validation-report.json`：六份展开配置通过 Draft 2020-12 结构校验及适用的静态一致性检查；四个语义冲突反例、三个结构错误反例均被拒绝。已核对旧默认普通计分表及旧Rules/Engine源文件哈希未变。运行时验收案例、动态合法动作非空证明及bot能力检查尚未执行，因为本次没有接入引擎或bot；不能将此报告解释为所有混搭已可运行。

`tables/`提供Avondale、NeuralPlay Original和NeuralPlay Perfect的明确数值表（依据上传文档），可将table内容复制至scoring.normalTable。文件名称仅表示文档中所引平台的表格，不对“Original”的历史权威性作保证。
