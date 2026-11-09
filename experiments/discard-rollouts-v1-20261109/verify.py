"""Verify all four policies use the same held-out layouts and frozen discard choices."""
import gzip,hashlib,json,os,subprocess
from pathlib import Path
import xml.etree.ElementTree as ET

study=Path(__file__).resolve().parent;repo=study.parent.parent;root=study/'results'
prior=study.parent/'guard-benefits-v1-20261109/results';dotnet=os.environ.get('DOTNET','dotnet')
meta=json.loads((study/'metadata.json').read_text());summary=json.loads((study/'summary.json').read_text())
assert hashlib.sha256((prior/'selected-discards.json').read_bytes()).hexdigest()==meta['PriorCasesSha256']
assert (root/'selected-discards.json').read_bytes()==(prior/'selected-discards.json').read_bytes()
for path,digest in meta['SourceHashes'].items():assert hashlib.sha256((repo/path).read_text().replace('\r\n','\n').encode()).hexdigest()==digest
runner=study/'bin/Release/net8.0/Study.dll'
assert hashlib.sha256(runner.read_bytes()).hexdigest().upper()==(root/'runner-version.txt').read_text()
assert all(r['pairedLayouts']==1024 and r['completeContexts']==128 and r['completeTemplateClusters']==94 for r in summary['policies'].values())
assert len(list((root/'continuations').glob('*/*.game.gz')))==4096
assert len(list((root/'continuations').glob('*/*.json')))==4096
assert not list(root.rglob('*.tmp'))
cases=json.loads((root/'selected-discards.json').read_text());paired_layouts=0
cards=lambda xs:sorted((c['Suit'],c['Rank']) for c in xs)
for case in cases:
    for layout in range(8):
        prior_deck=None
        for policy in ['weak','medium','fast','strong']:
            directory=prior if policy in ['medium','weak'] else root
            archives=[]
            for arm in [0,1]:
                prefix=directory/'continuations'/f"case-{case['Index']:04}"/f'layout-{layout}-{policy}-arm-{arm}'
                with gzip.open(str(prefix)+'.game.gz','rt') as f:archive=json.load(f)
                metric=json.loads(Path(str(prefix)+'.json').read_text());game=archive['Game'];deal=game['Rounds'][0]
                assert len(game['Rounds'])==1 and len(deal['TrickWinners'])==10 and sum(deal['Tricks'])==10
                assert game['Seed']==57119001+case['Index']*1009+layout*9176
                assert metric['Made']==deal['Made'] and metric['Tricks']==deal['Tricks'] and metric['ScoreDelta']==deal['ScoreDelta']
                assert metric['RelativeScore']==deal['ScoreDelta'][0]-deal['ScoreDelta'][1]
                assert cards(deal['Hands'][0]+deal['Kitty'])==cards(case['Hand'])
                actual=next(d['Discards'] for d in deal['Decisions'] if d['Phase']=='discard')
                assert cards(actual)==cards(case['Before'] if arm==0 else case['After'])
                if policy in ['fast','strong']:assert all(name==f'fixed-discard/{policy}-rules-v1' for name in game['Players'])
                if prior_deck is None:prior_deck=archive['Deck']
                assert archive['Deck']==prior_deck,'Policy changed hidden allocation'
                archives.append(archive)
            a,b=[x['Game']['Rounds'][0] for x in archives]
            assert a['Hands']==b['Hands'] and a['Kitty']==b['Kitty'];paired_layouts+=1
implementation=json.loads((root/'implementation-validation.json').read_text())
assert implementation['Cases']==128 and implementation['OriginalAndRepairedDiscardsUnchanged']
replay=subprocess.run([dotnet,str(runner),str(root),'verify',str(prior)],check=True,capture_output=True,text=True)
frozen=subprocess.run([dotnet,str(repo/'FiveHundredLab/bin/Release/net8.0/FiveHundredLab.dll'),'verify-frozen','--root',str(repo)],check=True,capture_output=True,text=True)
trx=Path(os.environ.get('DISCARD_ROLLOUTS_TRX','/tmp/discard-rollouts-validation/discard-rollouts.trx'))
counters=ET.parse(trx).find('.//t:Counters',{'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'})
assert counters.attrib['failed']=='0' and counters.attrib['passed']==counters.attrib['executed']
report={'newDealsReplayed':4096,'priorDealsReused':4096,'allPolicyPairedLayoutsVerified':paired_layouts,
        'templates':94,'fixedDecisions':128,'standaloneRepairMatchesPrior':True,
        'sameDecksAcrossAllFourPolicies':True,'replay':replay.stdout.strip(),'frozen':frozen.stdout.strip(),'tests':counters.attrib}
(study/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
