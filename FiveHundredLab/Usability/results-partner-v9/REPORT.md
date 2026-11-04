# 人类搭档体验代理测试 v1

固定局面、多个搜索种子与两种手牌顺序；不是实战失误频率或真人满意度。Critical 是本测试定义的明确体验约束，Advisory 是战术/约定提示，Control 不参与体验合格率。

| 场景 / 检查 | 级别 | visibility-fast-v8 | partner-fast-v9 |
|---|---|---:|---:|
| 无压力、无控制牌时避免高叫 / no-high-contract | Critical | 16/16 | 16/16 |
| 无压力、无控制牌时避免高叫 / prefer-pass | Advisory | 16/16 | 16/16 |
| 搭档已 Pass，对手高叫时不盲目争抢 / respect-warning | Advisory | 16/16 | 16/16 |
| 强红桃配合人类明确的红桃叫牌 / support-suit | Advisory | 16/16 | 16/16 |
| 对手均退出且搭档合约可出局，不无谓加叫 / leave-contract | Advisory | 16/16 | 16/16 |
| 人类用绝对大牌拿墩时，不白白跟掉 K / save-stopper | Critical | 12/16 | 16/16 |
| 搭档已稳拿当前墩，不轻易花 Joker 将吃 / conserve-trump | Advisory | 16/16 | 16/16 |
| 人类垫低梅花示意后，回引梅花 / return-requested-suit | Advisory | 8/16 | 16/16 |
| 利用人类已经公开的缺门，为其提供将吃机会 / offer-ruff | Advisory | 12/16 | 16/16 |
| 自己打 Misère，避免首引必拿的 A / no-certain-self-loss | Critical | 16/16 | 16/16 |
| 自己打 Misère，有低牌可躲时不主动吃墩 / duck-available | Critical | 16/16 | 16/16 |
| 被迫跟唯一的 A 时不能误报为失误 / forced-is-not-error | Control | 16/16 | 16/16 |
| 对手 Misère 正在拿墩，不能反过来救他 / cash-bust | Critical | 16/16 | 16/16 |
| 人类已经替防守方拿墩时，趁机处理危险高牌 / unload-high | Advisory | 16/16 | 16/16 |
| 面对公开 Misère，及时兑现可证明的必破机会 / take-public-certainty | Critical | 0/16 | 16/16 |
| Misère 拿到底牌 Joker 后必须丢掉 / bury-joker | Critical | 16/16 | 16/16 |
| 正常合约存在低副牌可弃时保留 Joker / retain-joker | Advisory | 16/16 | 16/16 |
| 无将弃牌避免拆成没有保护的单张 K / keep-king-guard | Advisory | 11/16 | 16/16 |

## 分层汇总

| 策略 | 明确约束通过 | 战术/约定提示通过 | 控制场景通过 | 非法动作/异常 |
|---|---:|---:|---:|---:|
| visibility-fast-v8 | 92/112 | 143/160 | 16/16 | 0 |
| partner-fast-v9 | 112/112 | 160/160 | 16/16 | 0 |

全牌分配只供审计；被测玩家不会额外获知其他玩家的隐藏手牌、未见底牌、其他玩家弃牌或固定发牌规则。本人按规则取得的底牌和自己的弃牌正常可见。不同级别不合并成一个总分。对约定相关的不符合，不能直接认定为程序错误。动作分布和每次种子详见 JSON/JSONL。

