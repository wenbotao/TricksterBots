"""Verify immutable sources, preserved strategy, paired decisions and regression results."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

study = Path(__file__).resolve().parent
repo = study.parent.parent
metadata = json.loads((study / 'metadata.json').read_text())
for path, digest in metadata['SourceHashes'].items():
    assert hashlib.sha256((repo / path).read_bytes()).hexdigest() == digest, path
reference = (repo / 'FiveHundredLab/Bots/FiveHundred/StrongDiscardRefinedBot.cs').read_text()
lean = (repo / 'FiveHundredLab/Bots/FiveHundred/StrongDiscardLeanBot.cs').read_text()
marker = '    public override PlayAction Play'
assert reference[reference.index(marker):] == lean[lean.index(marker):]
equivalence = json.loads((study / 'results/equivalence.json').read_text())
assert equivalence['Passed'] and equivalence['RepairStates'] == 256 and equivalence['FullRootSearches'] == 32
benchmark = json.loads((study / 'results/benchmark.json').read_text())
assert benchmark['Pairs'] == 24 and benchmark['AllDecisionsAndSearchesEqual']
for row in benchmark['Rows']:
    a, b = row['Reference'], row['Lean']
    assert a['Discard'] == b['Discard'] and a['Search'] == b['Search'] and a['Rollouts'] == b['Rollouts']
    assert a['StaticEvaluations'] == b['StaticEvaluations'] and b['FeatureEvaluations'] == 0
trx = Path(os.environ.get('LEAN_TRX', '/tmp/discard-lean-validation/full.trx'))
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
document = ET.parse(trx)
counters = document.find('.//t:Counters', ns).attrib
assert counters['total'] == counters['passed'] == counters['executed'] == '550' and counters['failed'] == '0'
new_tests = [r.attrib for r in document.findall('.//t:UnitTestResult', ns) if 'TestDiscardLean.' in r.attrib.get('testName', '')]
# MSTest's display name need not contain the class, so find declared methods as well.
if not new_tests:
    names = {'SequentialDecisionsAndSearchReportsMatchSelectedBaseline', 'IsolatedRepairDoesNotAdvanceSearchAndDiagnosticsAreCleared', 'DefaultAndEveryRuleConfigurationHaveIdenticalPlayedAndReplayedGames'}
    new_tests = [r.attrib for r in document.findall('.//t:UnitTestResult', ns) if r.attrib.get('testName') in names]
assert len(new_tests) == 3 and all(t['outcome'] == 'Passed' for t in new_tests)
frozen = subprocess.run([os.environ.get('DOTNET', 'dotnet'), str(repo / 'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'), 'verify-frozen', '--root', str(repo)], check=True, text=True, capture_output=True)
report = {'Passed': True, 'SourceHashesVerified': True, 'SearchPlayAndConfiguredSourceIdentical': True, 'Equivalence': equivalence,
          'FullTestCounters': counters, 'NewTests': new_tests, 'Frozen': frozen.stdout.strip(),
          'Benchmark': {k: v for k, v in benchmark.items() if k != 'Rows'},
          'Scope': 'Regression and engineering measurements; no new game-strength claim'}
(study / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
