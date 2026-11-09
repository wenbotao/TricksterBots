"""Replay every saved game and verify frozen strategy sources. No games are generated."""
import gzip
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

study = Path(__file__).resolve().parent
repo = study.parent.parent
dotnet = os.environ.get('DOTNET', 'dotnet')
files = sorted((study / 'results').glob('*/*/pair-*-leg-*.json.gz'))
assert files, 'No saved games'
assert len(files) == 32, 'Both fixed batches must be complete'
assert len(files) == len(list((study / 'results').glob('*/*/pair-*-leg-*.json')))
with tempfile.TemporaryDirectory(prefix='hand-guards-replay-') as temp:
    corpus = Path(temp) / 'games.jsonl'
    with corpus.open('w') as output:
        for path in files:
            with gzip.open(path, 'rt') as source:
                output.write(json.dumps(json.load(source), separators=(',', ':')) + '\n')
    replay = subprocess.run([dotnet, str(repo / 'FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll'), 'replay', str(corpus)], check=True, capture_output=True, text=True)
frozen = subprocess.run([dotnet, str(repo / 'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'), 'verify-frozen', '--root', str(repo)], check=True, capture_output=True, text=True)
report = {'savedGamesReplayed': len(files), 'replay': replay.stdout.strip(), 'frozen': frozen.stdout.strip()}
probes = json.loads((study / 'results/discard-probe.json').read_text())
assert len(probes) == 64 and len({p['Index'] for p in probes}) == 64
for p in probes:
    key = lambda c: (c['Suit'], c['Rank'])
    hand = set(map(key,p['Hand']))
    before = set(map(key,p['Before']))
    after = set(map(key,p['After']))
    assert len(hand)==13 and len(before)==3 and len(after)==3 and before<=hand and after<=hand
    assert not any((s=='Joker' or rank==14) and (s,rank) in after for s,rank in hand-before)
    assert p['Changed'] == (before!=after)
report['independentDiscardProbe'] = {'queries':len(probes),'changed':sum(p['Changed'] for p in probes),'legalAndControlsPreserved':True}
trx = Path(os.environ.get('HAND_GUARDS_TRX', '/tmp/hand-guards-validation/hand-guards.trx'))
if trx.exists():
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters = ET.parse(trx).find('.//t:Counters', ns)
    report['tests'] = dict(counters.attrib)
    assert int(counters.attrib['failed']) == 0
(study / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
