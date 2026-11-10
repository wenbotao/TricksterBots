"""Audit the pre-registered 540-match replication and generate its Chinese report.

Run from any directory after the matches, full regression and independent replay.
Only the 540 new matches are primary; the earlier 180 are a labelled secondary pool.
"""
from collections import Counter, defaultdict
from pathlib import Path
import gzip
import hashlib
import json
import csv
import sys

import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DATA = HERE / "results"
OLD = ROOT / "experiments/rules-v1-1-20261110/results"
read = lambda p: json.loads(p.read_text())
registration = read(DATA / "registration.json")
profiles = registration["Profiles"]
labels = dict(zip(profiles, ["Random", "RandomPass", "Weak", "Medium", "Fast", "Strong", "Partner", "StrongPlus", "StrongFlowCached"]))
plans = read(DATA / "plan.json")
old_plans = read(OLD / "plan.json")
assert len(plans) == registration["Matches"] == 540
assert len({p["Seed"] for p in plans}) == 540
assert not ({p["Seed"] for p in plans} & {p["Seed"] for p in old_plans})
bot_seeds = {seed for plan in plans for seed in plan["BotSeeds"]}
assert len(bot_seeds) == 2160
assert not (bot_seeds & {seed for plan in old_plans for seed in plan["BotSeeds"]})
for source, expected in registration["SourceHashes"].items():
    assert hashlib.sha256((ROOT / source).read_bytes()).hexdigest().upper() == expected, source
for source, expected in read(OLD / "final-source-hashes.json").items():
    assert hashlib.sha256((ROOT / source).read_bytes()).hexdigest().upper() == expected, source
assert not list(DATA.glob("*.failure.json"))
assert read(DATA / "independent-replay.json")["Validated"] == 540
for seat in range(4):
    for profile in profiles:
        assert sum(p["Profiles"][seat] == profile for p in plans) == 60

records = [read(DATA / f"game-{i:03d}.json") for i in range(540)]
old_records = [read(OLD / f"game-{i:03d}.json") for i in range(180)]
for i, row in enumerate(records):
    assert row["Plan"] == plans[i]
    assert row["Winner"] in (0, 1) and not row["Truncated"]
    with gzip.open(DATA / f"game-{i:03d}.game.gz", "rt") as stream:
        game = json.load(stream)
    assert game["Winner"] == row["Winner"] and len(game["Rounds"]) == row["Rounds"]
    assert game["Seed"] == plans[i]["Seed"] and game["InitialDealer"] == plans[i]["Dealer"]
    assert game["Rules"] == registration["Rules"]
    assert [r["Contract"] for r in game["Rounds"] if not r["Redeal"]] == [r["Contract"] for r in row["Contracts"]]


def arrays(rows):
    wins = np.zeros((len(rows), len(profiles)), dtype=int)
    appearances = np.zeros_like(wins)
    for i, row in enumerate(rows):
        for seat, profile in enumerate(row["Plan"]["Profiles"]):
            j = profiles.index(profile)
            appearances[i, j] += 1
            wins[i, j] += int(seat % 2 == row["Winner"])
    return wins, appearances


def summarize(rows, seed, earlier=None):
    wins, appearances = arrays(rows)
    totals = appearances.sum(axis=0)
    assert np.all(totals == len(rows) * 4 // 9)
    assert wins.sum() == len(rows) * 2
    rates = wins.sum(axis=0) / totals
    # Match-cluster bootstrap retains same-team and duplicate-profile dependence.
    # For the auxiliary pool, resample within each fixed-size batch separately.
    rng = np.random.default_rng(seed)
    if earlier is None:
        weights = rng.multinomial(len(rows), np.full(len(rows), 1 / len(rows)), size=20000)
    else:
        weights = np.concatenate([
            rng.multinomial(earlier, np.full(earlier, 1 / earlier), size=20000),
            rng.multinomial(len(rows) - earlier, np.full(len(rows) - earlier, 1 / (len(rows) - earlier)), size=20000),
        ], axis=1)
    samples = (weights @ wins) / (weights @ appearances)
    result = {}
    for j, profile in enumerate(profiles):
        result[profile] = {"Wins": int(wins[:, j].sum()), "Appearances": int(totals[j]),
                           "WinRate": float(rates[j]), "ClusterBootstrap95": np.quantile(samples[:, j], [.025, .975]).tolist()}
    differences = {}
    for a, b in [("strong-flow-cached-v1.1", "strong-plus-v1.1"), ("strong-flow-cached-v1.1", "partner-v1.1"),
                 ("strong-flow-cached-v1.1", "strong-v1.1"), ("partner-v1.1", "fast-v1.1"),
                 ("strong-plus-v1.1", "strong-v1.1"), ("strong-v1.1", "medium-v1.1")]:
        i, j = profiles.index(a), profiles.index(b)
        differences[a + "/" + b] = {"Difference": float(rates[i] - rates[j]),
                                    "ClusterBootstrap95": np.quantile(samples[:, i] - samples[:, j], [.025, .975]).tolist()}
    return {"Matches": len(rows), "Wins": result, "Differences": differences}


primary = summarize(records, 611119001)
secondary = summarize(old_records + records, 612119001, earlier=180)
contract_types = Counter()
contract_stats = {p: {"Attempts": 0, "Made": 0, "ContractorDelta": 0, "NormalLevels": Counter(), "Strains": Counter()} for p in profiles}
timings = {p: defaultdict(lambda: {"Calls": 0, "Milliseconds": 0., "AllocatedBytes": 0}) for p in profiles}
partner_types = {p: {"RandomTeammate": [0, 0], "OtherTeammate": [0, 0]} for p in profiles}
opposition = {p: {q: [0, 0] for q in profiles} for p in profiles}
position_counts = {p: [0, 0, 0, 0] for p in profiles}
duplicate_counts = {p: {"GamesPresent": 0, "GamesOnBothTeams": 0, "GamesWithRepeatedTeammates": 0} for p in profiles}
for row in records:
    for profile in set(row["Plan"]["Profiles"]):
        seats = [s for s, p in enumerate(row["Plan"]["Profiles"]) if p == profile]
        duplicate_counts[profile]["GamesPresent"] += 1
        duplicate_counts[profile]["GamesOnBothTeams"] += int(len({s % 2 for s in seats}) == 2)
        duplicate_counts[profile]["GamesWithRepeatedTeammates"] += int(any(sum(s % 2 == team for s in seats) == 2 for team in (0, 1)))
    for seat, profile in enumerate(row["Plan"]["Profiles"]):
        assert row["Metrics"][seat]["Profile"] == profile
        position_counts[profile][(seat - row["Plan"]["Dealer"] + 3) % 4] += 1
        won = int(seat % 2 == row["Winner"])
        teammate = row["Plan"]["Profiles"][(seat + 2) % 4]
        group = "RandomTeammate" if teammate in profiles[:2] else "OtherTeammate"
        partner_types[profile][group][0] += won
        partner_types[profile][group][1] += 1
        for opponent in (s for s in range(4) if s % 2 != seat % 2):
            value = opposition[profile][row["Plan"]["Profiles"][opponent]]
            value[0] += won
            value[1] += 1
        for phase, metric in row["Metrics"][seat]["Phases"].items():
            for key in metric:
                timings[profile][phase][key] += metric[key]
    for contract in row["Contracts"]:
        c = contract["Contract"]
        assert sum(contract["Tricks"]) == 10
        contract_types[c["Kind"]] += 1
        profile = row["Plan"]["Profiles"][contract["Declarer"]]
        value = contract_stats[profile]
        value["Attempts"] += 1
        value["Made"] += int(contract["Made"])
        value["ContractorDelta"] += contract["ScoreDelta"][contract["Declarer"] % 2]
        if c["Kind"] == "Normal":
            value["NormalLevels"][c["Tricks"]] += 1
            value["Strains"][c["Trump"]] += 1

audit = [json.loads(line) for line in (DATA / "compliance-audit.jsonl").read_text().splitlines()]
assert all(row["Passed"] for row in audit)
assert len({(r["Family"], r["Id"]) for r in audit}) == len(audit)
families = Counter(r["Family"] for r in audit)
assert families["bot-scenarios"] == 108 and families["protocol"] == 20 and families["bot-legality"] == 9
score_cases = sum(r["Detail"]["Distributions"] for r in audit if r["Family"] == "scoring")
tests = read(DATA / "test-results.json")
assert tests["Counters"]["failed"] == "0" and tests["Counters"]["passed"] == tests["Counters"]["total"]
assert len(tests["NewComplianceTests"]) == 55 and all(r["Outcome"] == "Passed" for r in tests["NewComplianceTests"])
frozen = {}
for manifest in [ROOT / "FiveHundredLab/frozen-baselines-v1.json", *sorted((ROOT / "FiveHundredLab").glob("frozen-profiles-*.json"))]:
    for source, expected in read(manifest).items():
        actual = hashlib.sha256((ROOT / source).read_bytes().decode().replace("\r\n", "\n").encode()).hexdigest()
        assert actual == expected, source
        frozen[source] = expected
summary = {"Primary540": primary, "Secondary720": secondary, "Contracts": dict(contract_types), "ContractorStats": contract_stats,
           "PartnerSubgroups": partner_types, "OppositionSeatPairCounts": opposition,
           "InitialAuctionPositionCounts": position_counts, "DuplicateProfileExposure": duplicate_counts,
           "NaturalPhaseTotalsDescriptiveOnly": timings,
           "Rounds": sum(r["Rounds"] for r in records), "Redeals": sum(r["Redeals"] for r in records),
           "Compliance": {"AuditEntries": len(audit), "Families": dict(families), "ScoringDistributions": score_cases},
           "Statistics": {"BootstrapReplicates": 20000, "PrimarySeed": 611119001, "SecondarySeed": 612119001,
                          "ResamplingUnit": "Whole match; auxiliary pool stratified by original 180/new 540 batches",
                          "MultipleComparisonCorrection": False, "Python": sys.version, "NumPy": np.__version__},
           "EntrypointAudit": read(DATA / "entrypoint-audit.json")}
(HERE / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
with (HERE / "win-rates.csv").open("w", newline="") as stream:
    writer = csv.writer(stream)
    writer.writerow(["Bot", "NewWins", "NewAppearances", "NewWinRate", "New95Low", "New95High", "PooledWins", "PooledAppearances", "PooledWinRate"])
    for profile in profiles:
        new, pool = primary["Wins"][profile], secondary["Wins"][profile]
        writer.writerow([labels[profile], new["Wins"], new["Appearances"], new["WinRate"], *new["ClusterBootstrap95"], pool["Wins"], pool["Appearances"], pool["WinRate"]])
validation = {"Passed": True, "Matches": 540, "Truncated": 0, "Errors": 0, "AppearancesPerProfile": 240, "AppearancesPerPhysicalSeat": 60,
              "GameSeedsDisjointFromEarlier180": True, "UniqueBotSeeds": 2160, "BotSeedsDisjointFromEarlier180": True,
              "SourceHashesMatchRegistration": True, "IndependentReplay": read(DATA / "independent-replay.json"),
              "FullRegression": tests["Counters"], "NewComplianceTests": 55, "ComplianceAuditEntries": len(audit),
              "ScoringDistributions": score_cases, "FrozenFiles": len(frozen), "StrategiesModifiedDuringStudy": False}
(DATA / "validation.json").write_text(json.dumps(validation, ensure_ascii=False, indent=2) + "\n")

pct = lambda x: f"{100*x:.2f}%"
interval = lambda x: f"{100*x[0]:.2f}～{100*x[1]:.2f}"
leaders = sorted(profiles, key=lambda p: primary["Wins"][p]["WinRate"], reverse=True)[:3]
findings = "新增批次胜率点估计前三为" + "、".join(f"{labels[p]} {pct(primary['Wins'][p]['WinRate'])}" for p in leaders) + "。"
for pair in ["strong-flow-cached-v1.1/strong-plus-v1.1", "strong-flow-cached-v1.1/partner-v1.1", "strong-v1.1/medium-v1.1"]:
    a, b = pair.split("/")
    v = primary["Differences"][pair]
    lo, hi = v["ClusterBootstrap95"]
    interpretation = "区间跨零，尚不能确认差异" if lo <= 0 <= hi else "未校正区间有前者占优迹象" if lo > 0 else "未校正区间有后者占优迹象"
    findings += f"{labels[a]}对{labels[b]}的参赛胜率差为{100*v['Difference']:+.2f}个百分点，{interpretation}。"
lines = ["# v1.1 扩展合规检查与 540 场独立随机对战", "",
         "日期：2026-11-10。先将 v1.1 更新以 `7005da4d` 同步至现有 GitHub 分支，再进行本轮独立验证。比赛期间没有修改引擎、九版 bot 的策略、搜索预算或默认规则。", "",
         "## 结论与验证范围", "",
         f"新增 540 场全部正常结束、无异常或截断，并通过逐场回放及赛后独立回放。每版 240 个座位参赛人次、每个物理座位 60 次。完整回归 {tests['Counters']['passed']}/{tests['Counters']['total']} 通过，其中本次新增 55 个测试行，另附 {len(audit)} 条场景审计记录、{score_cases:,} 次计分断言（包含跨测试重复分配）。", "",
         "主分析只使用新增 540 场；之前 180 场仅列为同规则的辅助合并样本，合计 720 场、每版 320 人次。旧规则下的 1,500 场及其他策略实验没有合并。", "",
         findings, "",
         "此前180场中Flow为72.50%、Partner为66.25%；独立540场分别为57.50%和52.50%，先前的领先没有重复。期间没有改动策略，不能把批次点估计变化解释为代码退化；小样本、不同发牌和搭档/对手组合都会影响排序。当前不宜继续依据前180场把Flow列为胜率最优。StrongPlus虽在新批次点估计最高，但它对Strong的差异区间仍跨零。", "",
         "## 特定场景合规", "",
         "| 检查方向 | 范围及断言 |", "| --- | --- |",
         "| 计分 | 澳/美全部普通阶数与花色、四个定约座位、十墩在四席之间的全部286种分配；独立算式检查普通分值、250分保底与防守计分。另检查四种零墩、独战和无合约NT的个人/全队目标及正负分。包括真实三人合约中不可能出现的队友获墩分配，用于算子鲁棒性，不能当实战覆盖数。 |",
         "| Joker | 5种领牌条件、3种跟牌模式×有无缺门×是否曾领该门×缺门强制×NT/零墩；低Joker与命名优先级；命名所有权与规则JSON往返。 |",
         "| 竞叫与配置 | 澳/美Open的5种序位；Grand必须由已叫普通Misère的队友支持；强制庄家不允许Pass；Inkle不会成为正式合约。另以显式七阶资格门槛的美式自定义配置验证Inkle不冒充正式历史；拒绝互相矛盾的特殊合约人数、目标、取底流程和开关。 |",
         "| 完整流程 | 5种合约×4个轮换庄位；验证参与人数、出牌张数、Grand顺次取底、独战交牌及额外弃牌；首墩结束后明牌及对手私有信息隔离。既有测试还检查开局明牌、Ace–no face申请和旧接口兼容。 |",
         "| bot变体执行 | 9版×澳/美2预设×6场景=108副完整脚本局；包括命名Joker NT、低Joker明牌零墩、No Pickup、Grand、独战和全Pass无合约NT。脚本控制竞叫、可选提名及独战开关，弃牌/交牌/出牌由实际bot完成。另9版真实叫牌验证强制庄家。 |",
         "| 非法与隔离 | 8种错误动作必须拒绝且不为失败副结算；修改玩家观察副本不能改变权威状态；篡改分数、赢墩或底牌的牌谱必须被回放拒绝。 |",
         "| 缓存一致性 | NT/低Joker零墩/Grand/独战中逐行动对照缓存及未缓存续演的轨迹和效用；既有测试另覆盖普通有将。 |", "",
         "扩展脚本的首轮运行出现了测试自身的预设假设错误：美式脚本误叫六阶NT，且把美式默认Misère误认为有先前七阶资格门槛。修正脚本为读取合法最低NT、在需要测试门槛时显式配置后重新完整运行。另按需求“前三人都Pass”的字面条件检查强制庄家；Inkle是信号叫牌，不能当作Pass。没有为通过测试而修改被测策略或引擎。", "",
         "**组合语义需要注意：** 美式Inkle不是正式合约，也不是Pass。当前强制庄家只在前三人确实全Pass时触发；若前面只有Inkle而无正式叫牌，仍可能流局。若产品需要“凡无人正式叫牌就强制庄家”，应另行明确扩展该条件，不能与当前开关混称。", "",
         "这些检查验证了指定场景下的规则执行与动作合法性，没有穷举所有JSON混搭组合，也不证明特殊合约的策略强度。引擎回放复用了权威规则代码；独立计分算式和明确边界断言补充其同源局限。", "",
         "## 新增540场：主分析", "",
         "每个物理座位分别打乱60份九版清单，允许同场重复类型；每席独立实例与随机种子，初庄轮换。540场的阵容、发牌种子、bot种子与源码指纹在首场前固定；独立于前180场，无按结果筛选或显著性提前停止，单场最多200副。", "",
         "置信区间按整场比赛bootstrap 20,000次，保留队友共赢及同类型重复相关性。这是该随机搭档生态中的座位参赛胜率，并非固定搭档的成对直接对打。阵容虽逐席平衡，具体队友/对手组合仍有随机差异；初始竞叫相对位置也是随机平衡而非逐项完全相等，计数保存在summary。该区间是固定平衡随机阵容设计下的探索性近似。", "",
         "| Bot | 胜/负（240人次） | 胜率 | 比赛聚类95%区间 |", "| --- | ---: | ---: | --- |"]
for p in sorted(profiles, key=lambda p: primary["Wins"][p]["WinRate"], reverse=True):
    v = primary["Wins"][p]
    lines.append(f"| {labels[p]} | {v['Wins']}/{240-v['Wins']} | {pct(v['WinRate'])} | {interval(v['ClusterBootstrap95'])}% |")
lines += ["", "![新增540场胜率及比赛聚类区间](win-rates.png)", "", "### 重点差异", "",
          "下表为生态参赛胜率差，区间未做多重比较校正，仅作探索性比较；不能把差值解释为两版直接对打的胜率。", "",
          "| 比较（前者−后者） | 差值 | 95%区间（百分点） |", "| --- | ---: | --- |"]
for pair, v in primary["Differences"].items():
    a, b = pair.split("/")
    lines.append(f"| {labels[a]} / {labels[b]} | {100*v['Difference']:+.2f} pp | {100*v['ClusterBootstrap95'][0]:+.2f}～{100*v['ClusterBootstrap95'][1]:+.2f} |")
lines += ["", "## 辅助：前180场与累计720场", "",
          "此前180场已在开发后看过结果，不能当作新的独立验证；主结论应优先看新增540场。合并区间分别在180和540两个批次内重采样，保持预先固定的批次大小。", "",
          "前批次生成后曾补充旧IPlayer未实现申请接口时的默认拒绝记录及配置版本保护，详见[原实验来源说明](../rules-v1-1-20261110/README.md#验证限制与后续)。默认Ace–no face关闭且九版均实现新接口，未改变该批默认对局决策；前180场已由最终引擎回放一致。本轮同时核验此前final-source-hashes与当前源码一致，不把生成/最终源码的差别隐去。", "",
          "| Bot | 前180场（80人次） | 新540场（240人次） | 累计胜/参赛 | 累计胜率及95%区间 |", "| --- | ---: | ---: | ---: | --- |"]
ow, oa = arrays(old_records)
for j, p in enumerate(profiles):
    v = secondary["Wins"][p]
    lines.append(f"| {labels[p]} | {pct(ow[:,j].sum()/oa[:,j].sum())} | {pct(primary['Wins'][p]['WinRate'])} | {v['Wins']}/320 | {pct(v['WinRate'])}（{interval(v['ClusterBootstrap95'])}%） |")
lines += ["", "## 定约行为与样本边界", "",
          f"新增样本共 {summary['Rounds']} 次发牌、{summary['Redeals']} 次重发，{summary['Rounds']-summary['Redeals']} 副进入打牌。定约类型：" + "、".join(f"{k} {v}副" for k, v in contract_types.items()) + "。新特殊合约默认关闭。自然零墩数量有限，不能从普通随机对战推断其胜率。", "",
          "| Bot | 定约副数 | 成约率 | 定约队平均得失分/副 | 平均普通叫牌阶数 |", "| --- | ---: | ---: | ---: | ---: |"]
for p in profiles:
    v = contract_stats[p]
    normal_attempts = sum(v["NormalLevels"].values())
    average_level = f"{sum(int(k)*n for k, n in v['NormalLevels'].items()) / normal_attempts:.2f}" if normal_attempts else "—"
    made = pct(v["Made"]/v["Attempts"]) if v["Attempts"] else "—"
    delta = f"{v['ContractorDelta']/v['Attempts']:+.2f}" if v["Attempts"] else "—"
    lines.append(f"| {labels[p]} | {v['Attempts']} | {made} | {delta} | {average_level} |")
lines += ["", "RandomPass没有主动定约，是策略定义，不是遗漏。成约率和定约得失分受拿牌、抢标及竞叫选择影响，不能单独据此排名；定约队分数也不是扣除对手得分后的净胜分。", "",
          "行为层面，Flow承担625副定约，成约率64.48%、定约队平均+35.57分；Plus承担544副，成约率72.61%、平均+70.97分。这提示后续优先对照竞叫取舍、弃牌候选和续演评估，但本轮没有模块消融或相同阵容/发牌的替换配对，不能直接认定差距来自某一模块，更不能归因于缓存本身。", "",
          "### 搭档结构敏感性（描述性）", "",
          "Random/RandomPass合称随机搭档，其余为其他搭档。子组并非成对控制，不能解释为某bot对弱队友的因果适应性；小样本百分比仅帮助检查阵容影响，详见summary中的同场对手座位对计数。", "",
          "| Bot | 随机搭档胜/参赛 | 其他搭档胜/参赛 |", "| --- | ---: | ---: |"]
for p in profiles:
    cells = []
    for key in ["RandomTeammate", "OtherTeammate"]:
        w, n = partner_types[p][key]
        cells.append(f"{w}/{n}（{pct(w/n)}）" if n else "—")
    lines.append(f"| {labels[p]} | " + " | ".join(cells) + " |")
lines += ["", "## 发现的入口集成缺口", "",
          "旧 `FiveHundredArena run` 命令尚未统一到当前默认与bot工厂：不传--rules时仍创建旧Rules及旧bot；显式传v1.1 JSON后引擎变为1.1，但bot仍是旧RuleAwarePlayer。后者也未转发新的可选申请/独战/交牌接口，不能当作九版V11策略的等价入口。各做两场、每场限制一副的冒烟已确认实际版本及名称，记录见[入口审计](results/entrypoint-audit.json)，原始烟测牌谱另存cli-*-smoke.jsonl.gz。这4场故意在一副后截断，仅检查入口，不纳入540场胜率或无截断统计。", "",
          "这是仍待统一的命令入口问题，不能用638项回归通过掩盖。本轮比赛通过显式RuleConfiguration.Load、Profiles.CreateForRules及Engine(rules)调用新版，不受影响；当前使用示例见[V11入口](../../FiveHundredLab/Bots/FiveHundred/V11/README.md)。本轮未修改入口或切换研究实现，后续应补上统一入口的集成测试。", "",
          "## 成本与后续建议", "",
          "Partner对Fast的参赛胜率差为−0.83个百分点，区间跨零。本轮没有证实其胜率优势或明显下滑；它面向真人搭档的价值仍应结合[此前体验探针](../rules-v1-1-20261110/README.md#体验检查)，不能仅按混合阵容胜率决定。", "",
          "本轮四个互斥分片并行运行，合规及完整回归部分时间也在运行。自然决策调用次数、累计耗时和线程累计分配均保存在原始结果与summary，供定位阶段使用；不作为纯CPU基准或与此前毫秒数直接比较。没有在本轮重做耗时基准。此前v1.1同局面串行结果仍见[初次报告](../rules-v1-1-20261110/README.md#同局面串行成本)。", "",
          "特殊合约的合法执行已获得更广覆盖；下一步强度评估应为Open Misère和新增特殊合约单独设计自然可达、固定发牌且交换席位的对照。对普通胜率差仍应结合区间，不按一次点估计宣布通用最强。工程热点仍是续演反复构造Medium及校验配置，可单独立项，在逐动作一致性验证后重新测成本。", "",
          "## 数据、校验与复现", "",
          f"源码指纹与事前注册一致，冻结源码/模型 {len(frozen)} 项校验通过。无赛后策略补丁、没有把失败局或超时局排除后重跑；本轮未出现这两类记录。540场每场均保留阵容、种子、全信息审计牌谱和统计。完整牌谱含隐藏牌，仅用于离线审计，不可作为玩家观察。", "",
          "文件：[完整摘要](summary.json)、[胜率CSV](win-rates.csv)、[验证结果](results/validation.json)、[事前注册](results/registration.json)、[阵容/种子计划](results/plan.json)、[合规审计](results/compliance-audit.jsonl)、[回归测试明细](results/test-results.json)、[脚本开发及修正记录](results/test-development.json)、[独立回放](results/independent-replay.json)、[测试源码](../../TestBots/TestV11Compliance.cs)。每场为results/game-NNN.json与game-NNN.game.gz。", "",
          "复现需.NET 8及Python NumPy/Matplotlib。从仓库根目录运行；已有结果先备份，prepare拒绝覆盖注册，审计JSONL必须使用不存在的新路径：", "",
          "```sh", 'V11_COMPLIANCE_AUDIT=/tmp/v11-reproduced-audit.jsonl dotnet test TestBots -c Release --logger "trx;LogFileName=full-expanded.trx" --results-directory /tmp/v11-reproduced-tests',
          "dotnet build experiments/v11-compliance-expanded-20261110 -c Release",
          "dotnet experiments/v11-compliance-expanded-20261110/bin/Release/net8.0/Study.dll prepare",
          "# run 0/1/2/3各启动一次；已保存完成记录可续跑，启动时核对源码指纹",
          "dotnet experiments/v11-compliance-expanded-20261110/bin/Release/net8.0/Study.dll run 0",
          "python3 experiments/v11-compliance-expanded-20261110/finalize.py /tmp/v11-reproduced-tests/full-expanded.trx /tmp/v11-reproduced-audit.jsonl",
          "python3 experiments/v11-compliance-expanded-20261110/analyze.py",
          "python3 experiments/v11-compliance-expanded-20261110/plot.py", "```", ""]
(HERE / "README.md").write_text("\n".join(lines))
print(json.dumps(validation, ensure_ascii=False, indent=2))
print(json.dumps(primary, ensure_ascii=False, indent=2))
