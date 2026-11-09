"""Verify preregistered natural matches, raw records, subject telemetry and analysis completeness."""
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess

study = Path(__file__).resolve().parent
repo = study.parent.parent
root = study/'results'
read = lambda p: json.loads(p.read_text())
def zipped(p):
    with gzip.open(p,'rt') as f:
        return json.load(f)
registration = read(root/'registration.json')
assert registration['RegisteredBeforeMatches']
for name,digest in registration['SourceHashes'].items():
    assert hashlib.sha256((repo/name).read_bytes()).hexdigest().upper()==digest
runner = study/'bin/Release/net8.0/Study.dll'
assert hashlib.sha256(runner.read_bytes()).hexdigest().upper()==(root/'runner-version.txt').read_text()
plans = read(root/'plan.json');assert len(plans)==32
assert len({p['GameSeed'] for p in plans})==32
assert all(sum(p['SubjectSeat']==seat for p in plans)==8 for seat in range(4))
assert all(sum(p['Dealer']==seat for p in plans)==8 for seat in range(4))
assert len(list(root.glob('matches/block-*/complete.json')))==32
assert not list(root.rglob('*.tmp')) and not list(root.rglob('*.failure.json'))
subject_decisions = 0;rounds=0;completed=0
for plan in plans:
    for arm in ['unpatched','guard','lean']:
        directory=root/'matches'/f"block-{plan['Id']:03}"
        metric=read(directory/(arm+'.json'));game=zipped(directory/(arm+'.game.gz'));calls=zipped(directory/(arm+'.calls.gz'))
        assert metric['ReplayPassed'] and metric['GameSeed']==plan['GameSeed']==game['Seed']
        assert metric['SubjectSeat']==plan['SubjectSeat'] and metric['Dealer']==plan['Dealer']==game['InitialDealer']
        assert metric['Roster']==plan['Roster'] and metric['BotSeeds']==plan['BotSeeds']
        assert len(calls)==metric['Calls'] and len(game['Rounds'])==metric['Rounds']
        assert metric['Truncated']==game['Truncated'] and metric['Winner']==game['Winner']
        assert metric['SubjectWin']==(game['Winner']==plan['SubjectSeat']%2)
        assert metric['Scores']==game['Rounds'][-1]['Scores']
        assert metric['RelativeScore']==metric['Scores'][plan['SubjectSeat']%2]-metric['Scores'][1-plan['SubjectSeat']%2]
        assert metric['PlayedDeals']==sum(not r['Redeal'] for r in game['Rounds'])
        assert metric['SubjectContracts']==sum(not r['Redeal'] and r['Declarer']==plan['SubjectSeat'] for r in game['Rounds'])
        assert metric['RepairEvents']==sum(bool(c['Guard'] and c['Guard']['Changed']) for c in calls)
        assert abs(sum(c['Milliseconds'] for c in calls)-metric['SubjectMilliseconds'])<1e-5
        assert sum(c['AllocatedBytes'] for c in calls)==metric['SubjectAllocatedBytes']
        trace=[(c['Round'],c['Phase'],json.loads(c['Action'])) for c in calls if c['Phase'] in ['bid','discard','play']]
        recorded=[]
        for r in game['Rounds']:
            for d in r['Decisions']:
                if d['Seat']!=plan['SubjectSeat'] or d['Phase'] not in ['bid','discard','play']:
                    continue
                recorded.append((r['Number'],d['Phase'],d[{'bid':'Bid','discard':'Discards','play':'Play'}[d['Phase']]]))
        assert trace==recorded, (plan['Id'],arm,'Telemetry actions differ from actual game record')
        assert all(c['Milliseconds']>=0 and c['AllocatedBytes']>=0 for c in calls)
        subject_decisions+=len(trace);rounds+=len(game['Rounds']);completed+=int(not game['Truncated'])
replay=subprocess.run([os.environ.get('DOTNET','dotnet'),str(runner),str(root),'verify'],check=True,text=True,capture_output=True)
summary=read(study/'summary.json');assert summary['Complete'] and summary['FinishedMatches']==96 and summary['CompletedBlocks']==32
for arm in summary['Arms']:
    assert summary['Arms'][arm]['Matches']==32
frozen=subprocess.run([os.environ.get('DOTNET','dotnet'),str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,text=True,capture_output=True)
report={'Passed':True,'Matches':96,'CompletedMatches':completed,'TruncatedMatches':96-completed,'PairedBlocks':32,'RawRounds':rounds,
        'SubjectDecisionsMatchedToRecords':subject_decisions,'MetadataAndSourceHashesVerified':True,'NoFailureOrIncompleteFiles':True,
        'Replay':replay.stdout.strip(),'Frozen':frozen.stdout.strip(),'Details':read(root/'replay-validation.json')}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
