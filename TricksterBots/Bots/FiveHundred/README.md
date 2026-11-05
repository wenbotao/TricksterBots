# Five Hundred bot 入口

本目录保留原.NET Standard实现与Medium兼容基线。

六个带完整规则配置接口的正式.NET 8 bot统一位于 [FiveHundredLab/Bots/FiveHundred](../../../FiveHundredLab/Bots/FiveHundred/README.md)：RandomBot、WeakBot、MediumBot、FastBot、StrongBot、PartnerBot。

保留两个目标框架，避免给原WebAPI引入反向依赖；正式新入口共用IPlayer和RuleConfiguration，旧FiveHundredBot.cs仍可用于现有调用方。
