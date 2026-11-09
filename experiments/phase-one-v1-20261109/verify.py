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
assert len(files) == len(list((study / 'results').glob('*/*/pair-*-leg-*.json')))
with tempfile.TemporaryDirectory(prefix='phase1-replay-') as temp:
    corpus = Path(temp) / 'games.jsonl'
    with corpus.open('w') as output:
        for path in files:
            with gzip.open(path, 'rt') as source:
                output.write(json.dumps(json.load(source), separators=(',', ':')) + '\n')
    replay = subprocess.run([dotnet, str(repo / 'FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll'), 'replay', str(corpus)], check=True, capture_output=True, text=True)
frozen = subprocess.run([dotnet, str(repo / 'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'), 'verify-frozen', '--root', str(repo)], check=True, capture_output=True, text=True)
report = {'savedGamesReplayed': len(files), 'replay': replay.stdout.strip(), 'frozen': frozen.stdout.strip()}
trx = Path(os.environ.get('PHASE_ONE_TRX', '/tmp/phase1-validation/phase-one.trx'))
if trx.exists():
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters = ET.parse(trx).find('.//t:Counters', ns)
    report['tests'] = dict(counters.attrib)
    assert int(counters.attrib['failed']) == 0
(study / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
