"""Replay complete conditioned deals; a one-round match cap is not an incomplete deal."""
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

study = Path(__file__).resolve().parent
repo = study.parent.parent
dotnet = os.environ.get('DOTNET', 'dotnet')
plan = json.loads((study / 'results/plan.json').read_text())
files = sorted((study / 'results/deals').glob('*/*.json'))
assert len(files) == len(plan['Jobs']) == 120
assert not list((study / 'results').rglob('*.tmp'))
for path, expected in json.loads((study / 'metadata.json').read_text())['SourceHashes'].items():
    assert hashlib.sha256((repo / path).read_text().replace('\r\n', '\n').encode()).hexdigest() == expected
with tempfile.TemporaryDirectory(prefix='proof-split-replay-') as temporary:
    corpus = Path(temporary) / 'deals.jsonl'
    with corpus.open('w') as output:
        for path in files:
            metrics = json.loads(path.read_text())
            assert metrics['CompletedDeal']
            with gzip.open(str(path)+'.gz', 'rt') as source:
                record = json.load(source)
            assert len(record['Rounds']) == 1 and len(record['Rounds'][0]['TrickWinners']) == 10
            output.write(json.dumps(record, separators=(',', ':'))+'\n')
    replay = subprocess.run([dotnet, str(repo / 'FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll'), 'replay', str(corpus)], check=True, capture_output=True, text=True)
frozen = subprocess.run([dotnet, str(repo / 'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'), 'verify-frozen', '--root', str(repo)], check=True, capture_output=True, text=True)
trx = Path(os.environ.get('PROOF_SPLIT_TRX', '/tmp/proof-split-validation/proof-split.trx'))
counters = ET.parse(trx).find('.//t:Counters', {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'})
assert int(counters.attrib['failed']) == 0 and int(counters.attrib['executed']) == int(counters.attrib['passed'])
report = {'savedCompletedDealsReplayed': len(files), 'replay': replay.stdout.strip(), 'frozen': frozen.stdout.strip(), 'tests': counters.attrib}
(study / 'validation.json').write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps(report, indent=2))
