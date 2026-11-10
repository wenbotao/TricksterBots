"""Verify raw game archives using the authoritative Arena replay CLI, independent of runner build hash."""
from collections import defaultdict, deque
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

study=Path(__file__).resolve().parent;repo=study.parent.parent;root=study/'results'
read=lambda p:json.loads(p.read_text())
def zipped(p):
    with gzip.open(p,'rt') as f:return json.load(f)
registration=read(root/'registration.json');assert registration['RegisteredBeforeMatches']
for path,digest in registration['SourceHashes'].items():assert hashlib.sha256((repo/path).read_bytes()).hexdigest().upper()==digest
plans=read(root/'plan.json');assert len(plans)==160 and len({p['GameSeed'] for p in plans})==160
assert sum(p['Group']=='replacement' for p in plans)==128
assert all(sum(p['Group']=='replacement' and p['SubjectSeat']==s for p in plans)==32 for s in range(4))
old_plan=read(study.parent/'incremental-vs-strongplus-20261109/results/plan.json')
assert not {p['GameSeed'] for p in old_plan}&{p['GameSeed'] for p in plans}
old_registration=read(study.parent/'incremental-vs-strongplus-20261109/results/registration.json')
assert registration['SourceHashes']==old_registration['SourceHashes']
assert not list(root.rglob('*.tmp')) and not list(root.rglob('*.failure.json'))
games=[];comparisons=[];traces=0
for p in plans:
    directory=root/p['Group']/f"block-{p['Id']:03}";assert (directory/'complete.json').exists()
    names=['incremental','plus'] if p['Group']=='replacement' else ['leg0','leg1'];paired=[];callsets={}
    for name in names:
        metric=read(directory/(name+'.json'));game=zipped(directory/(name+'.game.gz'));calls=zipped(directory/(name+'.calls.gz'))
        assert metric['Group']==p['Group'] and metric['Id']==p['Id'] and metric['Arm']==name
        assert metric['Seed']==game['Seed']==p['GameSeed'] and metric['Dealer']==game['InitialDealer']==p['Dealer']
        assert metric['Players']==game['Players'] and metric['Winner']==game['Winner'] and metric['Truncated']==game['Truncated']
        assert metric['Rounds']==len(game['Rounds']) and metric['PlayedDeals']==sum(not r['Redeal'] for r in game['Rounds'])
        for arm,subject in metric['Subjects'].items():
            cs=calls[arm];seat=subject['Seat'];scores=game['Rounds'][-1]['Scores']
            assert subject['Win']==(game['Winner']==seat%2) and subject['RelativeScore']==scores[seat%2]-scores[1-seat%2]
            assert subject['Calls']==len(cs) and subject['AllocatedBytes']==sum(c['AllocatedBytes'] for c in cs)
            assert abs(subject['Milliseconds']-sum(c['Milliseconds'] for c in cs))<1e-5
            assert subject['RepairEvents']==sum(bool(c['Guard'] and c['Guard']['Changed']) for c in cs)
            trace=[(c['Round'],c['Phase'],json.loads(c['Action'])) for c in cs if c['Phase'] in ['bid','discard','play']]
            recorded=[(r['Number'],d['Phase'],d[{'bid':'Bid','discard':'Discards','play':'Play'}[d['Phase']]]) for r in game['Rounds'] for d in r['Decisions'] if d['Seat']==seat and d['Phase'] in ['bid','discard','play']]
            assert trace==recorded,(p['Group'],p['Id'],name,arm,'telemetry mismatch')
            traces+=len(trace)
            for c in cs:
                assert c['Milliseconds']>=0 and c['AllocatedBytes']>=0 and c['Rollouts']>=0
                if arm=='plus':assert c['Rollouts']==(c['Search']['Samples']*c['Search']['Candidates'] if c['Search'] else 0)
        if p['Group']=='direct':
            assert metric['Subjects']['incremental']['Seat']+metric['Subjects']['plus']['Seat']==1
            if not game['Truncated']:assert metric['Subjects']['incremental']['Win']!=metric['Subjects']['plus']['Win']
        for r in game['Rounds']:
            if not r['Redeal']:assert len(r['TrickWinners'])==10 and sum(r['Tricks'])==10
        games.append(game);paired.append(game);callsets[name]=calls
    for a,b in zip(paired[0]['Rounds'],paired[1]['Rounds']):assert a['Hands']==b['Hands'] and a['Kitty']==b['Kitty'] and a['Dealer']==b['Dealer']
    if p['Group']=='replacement':
        left=callsets['incremental']['incremental'];right=callsets['plus']['plus']
        lookup=defaultdict(deque)
        for c in right:lookup[(c['Round'],c['Phase'],c['ContextHash'])].append(c)
        differences=[];identical=0
        for c in left:
            q=lookup[(c['Round'],c['Phase'],c['ContextHash'])]
            if not q:continue
            d=q.popleft()
            if c['Action']==d['Action']:identical+=1
            else:differences.append({k:c[k] for k in ['Round','Phase','Hand','Contract','Scores','LegalCount']}|{'Incremental':c['Action'],'Plus':d['Action']})
        i=0
        while i<min(len(left),len(right)) and left[i]['ContextHash']==right[i]['ContextHash'] and left[i]['Action']==right[i]['Action']:i+=1
        first=None
        if i<min(len(left),len(right)):
            assert left[i]['ContextHash']==right[i]['ContextHash'],'First divergence not explained by subject decision'
            first={'SameContext':True,'Incremental':left[i],'Plus':right[i]}
        else:
            assert len(left)==len(right) and paired[0]['Rounds']==paired[1]['Rounds'] and paired[0]['Winner']==paired[1]['Winner']
        comparisons.append({'Id':p['Id'],'AlignedContexts':identical+len(differences),'IdenticalActions':identical,'Differences':differences,'FirstDifference':first})
assert len(games)==320 and len(comparisons)==128
(root/'comparisons.json').write_text(json.dumps(comparisons,separators=(',',':')))
with tempfile.TemporaryDirectory(prefix='inc-plus-replay-') as directory:
    path=Path(directory)/'games.jsonl';path.write_text('\n'.join(json.dumps(g,separators=(',',':')) for g in games)+'\n')
    replay=subprocess.run([os.environ.get('DOTNET','dotnet'),str(repo/'FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll'),'replay',str(path)],check=True,capture_output=True,text=True)
microreg=read(root/'micro-registration.json');assert microreg['RegisteredBeforeTiming']
assert hashlib.sha256((root/'micro-cases.json').read_bytes()).hexdigest().upper()==microreg['InputSha256']
cases=read(root/'micro-cases.json');rows=read(root/'micro-benchmark.json');assert len(cases)==144 and len(rows)==432
byid=defaultdict(list)
old_cases=read(study.parent/'incremental-vs-strongplus-20261109/results/micro-cases.json')
old_hashes={hashlib.sha256(json.dumps(c['View'],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper() for c in old_cases}
assert len({c['PublicContextHash'] for c in cases})==144
assert not {c['PublicContextHash'] for c in cases}&old_hashes
from collections import Counter
assert all(v==12 for v in Counter((c['Bucket'],c['SourceArm']) for c in cases).values())
assert len(Counter((c['Bucket'],c['SourceArm']) for c in cases))==12
for c in cases:
    source=zipped(root/f"replacement/block-{c['SourceBlock']:03}/{c['SourceArm']}.calls.gz")[c['SourceArm']]
    digest=hashlib.sha256(json.dumps(c['View'],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
    assert c['PublicContextHash']==digest
    assert any(x['ContextHash']==digest and x['Phase']==c['Phase'] for x in source)
for r in rows:
    byid[r['Id']].append(r);c=cases[r['Id']]
    assert r['SameAction']==(r['Incremental']['Action']==r['Plus']['Action'])
    for arm in ['Incremental','Plus']:
        action=json.loads(r[arm]['Action'])
        if c['Phase']=='bid':assert action in c['Bids']
        elif c['Phase']=='play':assert action in c['Plays']
        else:assert len(action)==c['DiscardCount'] and len({(a['Suit'],a['Rank']) for a in action})==len(action) and all(x in c['View']['Hand'] for x in action)
        if arm=='Plus':assert r[arm]['Rollouts']==(r[arm]['Search']['Samples']*r[arm]['Search']['Candidates'] if r[arm]['Search'] else 0)
for rs in byid.values():
    assert len(rs)==3
    for arm in ['Incremental','Plus']:assert all(rs[0][arm]['Action']==r[arm]['Action'] and rs[0][arm]['Search']==r[arm]['Search'] and rs[0][arm]['Rollouts']==r[arm]['Rollouts'] for r in rs)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
trx=ET.parse(os.environ.get('COMPARISON_TRX','/tmp/strongplus-expanded-validation/full.trx'))
counters=trx.find('.//t:Counters',ns).attrib;assert counters['passed']==counters['executed']==counters['total']=='565' and counters['failed']=='0'
frozen=subprocess.run([os.environ.get('DOTNET','dotnet'),str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,capture_output=True,text=True)
report={'Passed':True,'ActualMatches':320,'ReplacementSeedRosterBlocks':128,'DirectMirroredSeedPairs':32,'Truncated':sum(g['Truncated'] for g in games),
    'SubjectDecisionsMatchedToActualRecords':traces,'PairedDealsVerified':True,'SourceHashesVerified':True,'SerialMicroStates':144,'SerialMicroMeasurements':432,
    'FreshReportDerivedPlusBudgetVerified':True,'MicroRepeatedActionsDeterministic':True,'FullTestCounters':counters,'Frozen':frozen.stdout.strip(),'Replay':replay.stdout.strip(),
    'GenerationRunnerSha256':(root/'runner-version.txt').read_text(),'ReplayEngineSha256':hashlib.sha256((repo/'FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll').read_bytes()).hexdigest()}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
