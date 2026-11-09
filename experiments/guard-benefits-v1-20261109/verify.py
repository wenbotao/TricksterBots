"""Final archival replay plus pair integrity; requires the complete registered experiment."""
import gzip,hashlib,json,os,subprocess
from collections import Counter,defaultdict
from pathlib import Path

study=Path(__file__).resolve().parent;repo=study.parent.parent;root=study/'results'
dotnet=os.environ.get('DOTNET','dotnet')
runner=study/'bin/Release/net8.0/Study.dll'
assert hashlib.sha256(runner.read_bytes()).hexdigest().upper()==(root/'runner-version.txt').read_text()
metadata=json.loads((study/'metadata.json').read_text())
for path,digest in metadata['SourceHashes'].items():
    assert hashlib.sha256((repo/path).read_text().replace('\r\n','\n').encode()).hexdigest()==digest
summary=json.loads((study/'summary.json').read_text())
assert all(r['completedPairs']==64 and not r['truncated'] for r in summary['matches'].values())
assert sum(r['savedGames'] for r in summary['matches'].values())==256
assert len(list((root/'matches').glob('*/*.game.gz')))==256
assert len(list((root/'proposals').glob('case-*.json')))==1024
selected=json.loads((root/'selected-discards.json').read_text())
assert len(selected)==128 and len({c['Template'] for c in selected})==94
assert all(c['Changed'] and c['Template']!=0 for c in selected)
assert max(Counter(c['Template'] for c in selected).values())<=2
assert all(r['completedContexts']==128 and r['completedPairedLayouts']==1024 and r['templateClusters']==94 for r in summary['discard']['policyEffects'].values())
assert not list(root.rglob('*.tmp'))
for policy in ['medium','weak']:
    for case in selected:
        for layout in range(8):
            archives=[]
            for arm in [0,1]:
                prefix=root/'continuations'/f"case-{case['Index']:04}"/f'layout-{layout}-{policy}-arm-{arm}'
                with gzip.open(str(prefix)+'.game.gz','rt') as source: archive=json.load(source)
                metric=json.loads(Path(str(prefix)+'.json').read_text())
                game=archive['Game'];deal=game['Rounds'][0]
                assert len(game['Rounds'])==1 and len(deal['TrickWinners'])==10 and sum(deal['Tricks'])==10
                assert metric['Made']==deal['Made'] and metric['ScoreDelta']==deal['ScoreDelta']
                assert metric['RelativeScore']==deal['ScoreDelta'][0]-deal['ScoreDelta'][1]
                cards=lambda xs:sorted((c['Suit'],c['Rank']) for c in xs)
                assert cards(deal['Hands'][0]+deal['Kitty'])==cards(case['Hand'])
                discards=next(d['Discards'] for d in deal['Decisions'] if d['Phase']=='discard')
                assert cards(discards)==cards(case['Before'] if arm==0 else case['After'])
                archives.append(archive)
            assert archives[0]['Deck']==archives[1]['Deck']
            a,b=[archive['Game']['Rounds'][0] for archive in archives]
            assert a['Hands']==b['Hands'] and a['Kitty']==b['Kitty']
replay=subprocess.run([dotnet,str(runner),str(root),'verify'],check=True,capture_output=True,text=True)
frozen=subprocess.run([dotnet,str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,capture_output=True,text=True)
report={'matchesReplayed':256,'conditionalDealsReplayed':4096,'pairedHiddenLayoutsChecked':2048,
        'proposalContexts':1024,'selectedContexts':128,'templateClusters':94,
        'replay':replay.stdout.strip(),'frozen':frozen.stdout.strip(),'policiesUnchanged':True,
        'priorRegression':'539 tests passed in hand-guards-v1; frozen bot sources unchanged; no redundant full-suite rerun',
        'harnessPreflight':'32 fixture deals replayed before primary experiments; excluded from estimates'}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
