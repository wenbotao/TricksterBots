# TricksterBots

This repository contains the source for for the _suggest_ methods of the Trickster Cards bots (computer players) along with a Web API driver for local testing.

## Requirements

- .NET 8
- Visual Studio Code (optional, but recommended)
- C# Dev Kit Extension for VS Code (optional, but recommended)
- GitHub Desktop (optional, but recommended)

## Getting Started

Create and clone a fork of this repository from which to submit pull requests. If not using GitHub Desktop, ensure you also clone submodules. If you're not familiar with GitHub's pull request process you can learn about using GitHub Desktop to accomplish this here:

- [Cloning and Forking Repositories](https://docs.github.com/en/desktop/contributing-and-collaborating-using-github-desktop/adding-and-cloning-repositories/cloning-and-forking-repositories-from-github-desktop)
- [Creating a Pull Request](https://docs.github.com/en/desktop/contributing-and-collaborating-using-github-desktop/working-with-your-remote-repository-on-github-or-github-enterprise/creating-an-issue-or-pull-request#creating-a-pull-request)

After installing the requirements and cloning this repository, open the cloned folder in VS Code. Install the "C# Dev Kit" extension when prompted. This will open a solution with 3 projects: BridgeBidder, TestBots, and TricksterBots.

Bot source is located in the Bots folder in the TricksterBots project. It is organized into game folders. Most games have only file file: *game*Bot.cs.

Documentation on the classes and interfaces used by the bots can be viewed at https://www.trickstercards.com/home/help/BotClasses.html.

## Testing

Unit testing is done using the TestBots project. These are the tests that will run automatically against any pull requests. You can use the Testing tab in VS Code to run the full suite or individual tests on your own machine. 

## FiveHundred battle testing

[FiveHundredArena](FiveHundredArena/README.md) provides a four-player Australian-rule match runner, weak/medium/strong/random-legal players, paired strategy comparisons, full JSONL game logs, statistics, and replay validation.

## FiveHundred performance experiments

[FiveHundredLab](FiveHundredLab/README.md) keeps previous bots frozen and adds versioned speed/quality profiles, phase profiling, matched-observation timing, and small paired-match experiments. Pretraining is deferred.

## FiveHundred 开发总结

[开发过程与成果总结](FIVEHUNDRED_DEVELOPMENT.md)介绍规则配置的实际效果、对战引擎流程、六种正式 bot 的策略、源码依赖关系及验证边界。

## FiveHundred 随机对战数据

[1,500场数据与胜率分析](experiments/fivehundred-random-mix-20261105/README.md)包含逐场结果、完整压缩牌谱、复现计划、统计脚本和分析图表。

## FiveHundred completed bots

[整体比较报告](FIVEHUNDRED_BOT_COMPARISON.md) covers the nine completed versions, including StrongFlowCached, with strategy and rule interfaces, matched-state performance, partner-experience probes, and separately qualified historical match results. [Formal bot sources](FiveHundredLab/Bots/FiveHundred/README.md).
