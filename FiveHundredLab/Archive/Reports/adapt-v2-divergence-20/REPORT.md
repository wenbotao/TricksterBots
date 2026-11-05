# 加强学习版：原20组重测与首次行为分歧

使用原20组种子2711001；三方同阵容、同玩家种子、同牌序，搜索预算相同。每场重置学习。另一次60组实验不并入本报告。

| 版本 | 胜:负 | 平均终局分差 | 平均决策ms |
|---|---:|---:|---:|
| Adapt-v2 | 13:7 | 46.0 | 40.88 |
| Adapt | 14:6 | 130.0 | 40.49 |
| Strong | 14:6 | 181.0 | 36.77 |

Adapt-v2独胜1组，Strong独胜2组。首次分歧分布：{'play': 2, 'bid': 15, 'identical': 3}。

以下只比较整条决策历史仍相同的首次分歧；两方当时自身手牌和公开信息相同。后续轨迹已经不同，不直接解释成同一局面的策略优劣。结果差异不证明首次动作是胜负原因。

| 组 | 首次分歧副/阶段 | Adapt-v2 | Strong | 最终结果(v2/Strong) |
|---:|---|---|---|---|
| 1 | 1/play | {'Card': {'Suit': 'Clubs', 'Rank': 7, 'IsJoker': False}, 'JokerLeadSuit': 'Unknown'} | {'Card': {'Suit': 'Clubs', 'Rank': 13, 'IsJoker': False}, 'JokerLeadSuit': 'Unknown'} | 胜/负 |
| 2 | 1/bid | 6Unknown Normal | 6Clubs Normal | 负/负 |
| 3 | 1/bid | 6Diamonds Normal | 6Hearts Normal | 负/负 |
| 4 | 1/bid | Pass | 7Diamonds Normal | 负/胜 |
| 5 | 无分歧 | - | - | 胜/胜 |
| 6 | 2/bid | 8Hearts Normal | 6Hearts Normal | 胜/胜 |
| 7 | 1/bid | 6Unknown Normal | 6Clubs Normal | 胜/胜 |
| 8 | 1/bid | Pass | 6Clubs Normal | 胜/胜 |
| 9 | 2/play | {'Card': {'Suit': 'Spades', 'Rank': 7, 'IsJoker': False}, 'JokerLeadSuit': 'Unknown'} | {'Card': {'Suit': 'Clubs', 'Rank': 9, 'IsJoker': False}, 'JokerLeadSuit': 'Unknown'} | 负/负 |
| 10 | 1/bid | 6Unknown Normal | Pass | 胜/胜 |
| 11 | 2/bid | 7Diamonds Normal | 6Diamonds Normal | 胜/胜 |
| 12 | 2/bid | Pass | 10Hearts Normal | 负/负 |
| 13 | 1/bid | Pass | 9Spades Normal | 负/负 |
| 14 | 2/bid | 6Unknown Normal | 6Clubs Normal | 负/胜 |
| 15 | 2/bid | 6Spades Normal | 6Unknown Normal | 胜/胜 |
| 16 | 无分歧 | - | - | 胜/胜 |
| 17 | 无分歧 | - | - | 胜/胜 |
| 18 | 1/bid | Pass | 6Hearts Normal | 胜/胜 |
| 19 | 1/bid | 6Clubs Normal | Pass | 胜/胜 |
| 20 | 1/bid | 7Spades Normal | 7Diamonds Normal | 胜/胜 |

强化参数：叫牌证据0.2→0.6、出牌0.3→0.6、先验回拉16%→4%、旧证据指数0.97→0.99、每玩家每副证据上限2→4、学习假设手牌8→16；根搜索仍8样本/12弃牌候选。

此处行为变化同时包含学习参数和学习采样数量的影响；没有进行逐动作反事实强制续演，不能单凭一个不同选择断言它更好。所有60场均由正式引擎验证并重放。

## 本次学习强化究竟改变了什么

对Strong的17组首次分歧（15次叫牌、2次出牌），旧Adapt当时都作出了与V2相同的选择。这些首次分歧来自原有自适应机制，不能归因于本次加大学习参数。

直接对比V2与旧Adapt，7组出现首次新分歧：第1/6/9组改叫花色，第19/20组由6♥改为Pass，第4/14组改变首引。其余13组整条决策轨迹相同。

两个值得复盘的局面：

- 第4组第2副，自己执行6♦。相同局面中旧Adapt首引J♥（左Bower），V2改引J♠。该副旧版拿6墩成约，V2拿5墩失败；整场旧Adapt和Strong都赢，V2输。
- 第14组第4副，自己执行7♠。旧Adapt领Joker，V2领♣9；该副旧版拿8墩成约，V2拿6墩失败。这组旧Adapt和V2最终都输，Strong赢。

这些是完整闭环续演后的结果，不是只替换一个动作、固定其后策略的反事实实验，因此不能把整场胜负全部归给该单步。不过它们明确指出了更强学习改变控制牌兑现时机的风险。详见v2-v1-first-differences.json和divergences.json。

本轮V2为13胜7负，旧Adapt与Strong均14胜6负，未观察到加强学习的对局收益。保留旧Adapt默认，不自动替换为V2。全套509项测试通过，54项冻结指纹验证通过。
