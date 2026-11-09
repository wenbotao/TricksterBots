"""Verify fresh holdout, logical-arm aliases, score overlays, and all physical replays."""
import gzip,hashlib,json,os,subprocess
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

study=Path(__file__).resolve().parent;repo=study.parent.parent;root=study/'results'
dotnet=os.environ.get('DOTNET','dotnet');meta=json.loads((study/'metadata.json').read_text())
for path,digest in meta['SourceHashes'].items():assert hashlib.sha256((repo/path).read_text().replace('\r\n','\n').encode()).hexdigest()==digest
summary=json.loads((study/'summary.json').read_text());assert summary['complete']
pool=json.loads((root/'hand-pool.json').read_text());selected=json.loads((root/'selected-hands.json').read_text())
assert len(pool)==192 and len(selected)==32 and Counter(h['Family'] for h in selected)=={'random':16,'perturb':16}
key=lambda cards:tuple(sorted((c['Suit'],c['Rank']) for c in cards))
prior=json.loads((repo/'experiments/guard-benefits-v1-20261109/results/discard-template-plan.json').read_text())['Hands']
assert len({key(h['Hand']) for h in pool})==192 and not {key(h['Hand']) for h in pool}&set(map(key,prior))
states=json.loads((root/'state-plan.json').read_text());assert len(states)==256
assert len(list((root/'decisions').glob('*.json')))==256
groups=0;physical=0;logical=0
for directory in sorted((root/'outcomes').glob('*')):
    if not (directory/'complete.json').exists():continue
    data=[json.loads(p.read_text()) for p in directory.glob('choice-*.json')]
    group_arms=[a for d in data for a in d['Arms']];assert sorted(group_arms)==['audit','original','shape','v1']
    archived=[]
    for path in directory.glob('choice-*.json'):
        metric=json.loads(path.read_text())
        with gzip.open(str(path).replace('.json','.game.gz'),'rt') as f:a=json.load(f)
        g=a['Game'];r=g['Rounds'][0];assert len(g['Rounds'])==1 and len(r['TrickWinners'])==10 and sum(r['Tricks'])==10
        assert r['Contract']['Tricks']==metric['Tricks'] and r['Contract']['Trump']=='Unknown'
        assert metric['Scores']==a['InitialScores'] and metric['Made']==r['Made'] and metric['ScoreDelta']==r['ScoreDelta']
        assert metric['RelativeScore']==r['ScoreDelta'][0]-r['ScoreDelta'][1]
        discard=next(d['Discards'] for d in r['Decisions'] if d['Phase']=='discard');assert discard==metric['Discard']
        proposal=json.loads((root/'decisions'/f"hand-{metric['Hand']:03}-condition-{metric['Condition']}.json").read_text())
        for arm in metric['Arms']:assert proposal['Choices'][arm]==discard
        assert a['PostScores']==[x+y for x,y in zip(a['InitialScores'],r['ScoreDelta'])]
        assert metric['Winner']==a['Winner'];assert metric['Utility']==(2000 if a['Winner']==0 else -2000) if a['Winner'] is not None else metric['Utility']==metric['RelativeScore']
        archived.append(a);physical+=1;logical+=len(metric['Arms'])
    assert all(a['Deck']==archived[0]['Deck'] and a['Game']['Rounds'][0]['Hands']==archived[0]['Game']['Rounds'][0]['Hands'] and a['Game']['Rounds'][0]['Kitty']==archived[0]['Game']['Rounds'][0]['Kitty'] for a in archived)
    groups+=1
assert groups==1024 and logical==4096 and physical==summary['physicalDeals']==2452
assert not list(root.rglob('*.tmp'))
runner=study/'bin/Release/net8.0/Study.dll';assert hashlib.sha256(runner.read_bytes()).hexdigest().upper()==(root/'runner-version.txt').read_text()
replay=subprocess.run([dotnet,str(runner),str(root),'verify'],check=True,capture_output=True,text=True)
frozen=subprocess.run([dotnet,str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,capture_output=True,text=True)
trx=ET.parse('/tmp/refinement-validation/discard-refinement.trx').find('.//t:Counters',{'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'})
assert trx.attrib['failed']=='0' and trx.attrib['executed']==trx.attrib['passed']
bench=json.loads((root/'engineering-benchmark.json').read_text());assert len(bench)==8 and all(b['SameDecision'] and b['Reference']['StaticEvaluations']==572 and b['Cached']['StaticEvaluations']==286 for b in bench)
report={'freshHands':32,'boundaryStates':256,'pairedGroups':groups,'physicalDealsReplayed':physical,'logicalArmDeals':logical,
        'scoreOverlayAndAliasesVerified':True,'noOldTemplateOverlap':True,'engineeringEquivalentDecisions':True,
        'replay':replay.stdout.strip(),'frozen':frozen.stdout.strip(),'tests':trx.attrib}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
