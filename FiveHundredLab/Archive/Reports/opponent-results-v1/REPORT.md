# 自适应 Strong 分阶段验证 v1

全部504项测试通过；49项冻结指纹验证通过。三阶段×六配置18次运行均通过搜索、合法性和重放检查。固定Medium消融通过一副同种子旧Strong决策对照。

| 模式 | 胜:负 | 截断 | 公开叫牌更新次数 | 出牌偏好更新次数 |
|---|---:|---:|---:|---:|
| FixedMedium | 1:1 | 0 | 0 | 0 |
| FixedMixture | 1:1 | 0 | 0 | 0 |
| Bidding | 1:1 | 0 | 99 | 0 |
| Belief | 1:1 | 0 | 56 | 0 |
| Play | 1:1 | 0 | 39 | 6 |

每模式仅两场同牌序换队，默认8样本/12弃牌候选。保守脚本队友、常规/激进脚本对手；不是对真人或总体胜率的估计。全部10场牌谱通过引擎重放。

## 预测检查

- Public conservative stream prequential NLL: prior=0.908, adaptive=0.919; actor hand only used by synthetic actor
- Controlled likelihood test NLL: 1.551 -> 0.358; not an actual-player calibration claim

受控固定似然改善不等于真实牌力推断成功。公开流的NLL没有改善，应保留实验状态，不宣称预测或胜率收益。

完整规则、各座位后验权重、更新次数和耗时在summary.json。解压games.jsonl.gz后可用FiveHundredArena replay验证。
