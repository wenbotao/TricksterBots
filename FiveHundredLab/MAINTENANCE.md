# 收尾整理记录

1. 效率：弃牌的Joker修正复用基础牌力，零墩危险度按决策缓存，隐藏牌匹配复用有效花色表。不改变随机数顺序、候选顺序、算术结合顺序或搜索预算。
2. 优化前采集36份行为指纹，包含真实叫牌返回和固定上下文下的整副牌；优化和正式类接入后逐一对照。
3. 六种正式类位于Bots/FiveHundred；默认行为基线位于Legacy或原Arena类。共享规则算法位于Arena/Bots/FiveHundred。
4. Adapt、预训练、过渡策略及旧实验报告移至Archive。保留兼容源码和回归资料，没有销毁复现证据。迁移对照表见FiveHundredRules/cleanup-relocations.json。
5. 规范核心流程格式，采用view/rules等参数名、RequireMatchingRules/SelectByRollout/AssignCard等行为名；为竞叫、有效花色、参与人数、信息边界和结算添加注释。
6. 冻结清单随物理路径迁移。优化前规则核心指纹保存在Archive/Reports/pre-cleanup-rule-strategy-hashes.json；本轮获授权的优化、格式和引擎注释更新现行指纹。其他历史策略文件不改变内容。

仅对代码内容和行为进行回归，不根据一次耗时声称普遍加速；减少重复计算是可确认的工作量变化。历史json中的文件路径和程序集指纹代表历史构建，不重新伪造为当前构建。

本轮最终验证：Release全量测试510/510通过；36份决策快照一致；61项冻结源码/模型校验通过；git diff --check通过。

## 后续：策略主体整合

六个正式 bot 的策略已移入各自文件，正式 Medium 完整迁入原算法并直接继承平台基础类；默认 Fast/Strong 的 Medium 基准及续演改用新文件内的实现。Partner 组合新 Fast，保留两条路径的体验判断。共享基类不再委托 RuleAwarePlayer。原 Legacy 策略移入 Archive/Strategies；CardFrame 工具移入 Shared。此前本文第3点描述的是整合前布局，现以 [CONSOLIDATION.md](CONSOLIDATION.md) 为准。

最终验证：Release 全量测试 **514/514 通过**，无失败或跳过；36 份配置行为快照一致；默认普通/零墩完整十墩对照18组；体验局面逐动作对照204次；新旧源码/模型指纹72项通过。机器记录见 [consolidation-validation.json](consolidation-validation.json)。
