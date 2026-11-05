import json,gzip,itertools
from pathlib import Path
import numpy as np
from scipy.optimize import minimize
from scipy.special import expit
from scipy.stats import norm,chisquare
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
r=Path(__file__).resolve().parent.parent;out=r/'analysis';names=['Weak','Medium','Fast','Strong','Partner'];N=1500
G=sorted([json.loads(p.read_text()) for p in r.glob('game-*.json')],key=lambda x:x['Number']);assert [g['Number'] for g in G]==list(range(1,N+1))
plan=json.loads((r/'plan.json').read_text());assert len(plan)==N
A=np.zeros((N,5),int);W=A.copy();C=np.zeros((N,2,5),int);y=np.array([g['Winner']==0 for g in G],int)
for i,g in enumerate(G):
 assert not g['Error'] and not g['Truncated'] and g['Winner'] in [0,1]
 assert all(g[k]==plan[i][k] for k in ['Number','Seed','Dealer','Lineup','BotSeeds'])
 for s,n in enumerate(g['Lineup']):
  j=names.index(n);A[i,j]+=1;C[i,s%2,j]+=1;W[i,j]+=int(s%2==g['Winner'])
assert A.sum()==6000 and W.sum()==3000
rng=np.random.default_rng(20261107);B=20000;bs=[]
for start in range(0,B,200):
 ix=rng.integers(0,N,size=(min(200,B-start),N));bs.append(W[ix].sum(1)/A[ix].sum(1))
bs=np.concatenate(bs);ci=np.quantile(bs,[.025,.975],axis=0);rates=W.sum(0)/A.sum(0)
# Team logistic model: one observation per match, team counts differ by zero in sum.
keep=[0,2,3,4];D=C[:,0,:]-C[:,1,:];X=np.column_stack([np.ones(N),D[:,keep]])
def fun(b):
 z=X@b;return np.logaddexp(0,z).sum()-y@z+1e-9*(b@b)
def jac(b):return X.T@(expit(X@b)-y)+2e-9*b
fit=minimize(fun,np.zeros(5),jac=jac,method='BFGS',options={'gtol':1e-7});b=fit.x;p=expit(X@b);cov=np.linalg.inv(X.T@((p*(1-p))[:,None]*X))
cov=cov @ (X.T @ (((y-p)**2)[:,None]*X)) @ cov
strength=np.zeros(5);strength[keep]=b[1:]
V=np.zeros((5,5));V[np.ix_(keep,keep)]=cov[1:,1:]
pairs=[]
for i,j in itertools.combinations(range(5),2):
 delta=strength[i]-strength[j];se=np.sqrt(V[i,i]+V[j,j]-2*V[i,j]);pv=2*norm.sf(abs(delta/se));pairs.append(dict(a=names[i],b=names[j],delta=float(delta),p=float(pv),probability=float(expit(delta)),ci=[float(expit(delta-1.96*se)),float(expit(delta+1.96*se))]))
order=sorted(range(10),key=lambda k:pairs[k]['p']);last=0
for rank,k in enumerate(order):last=max(last,min(1,(10-rank)*pairs[k]['p']));pairs[k]['holmP']=last
standard=[]
for i in range(5):
 vals=[]
 for t,o1,o2 in itertools.product(range(5),repeat=3):
  z=strength[i]+strength[t]-strength[o1]-strength[o2];vals.append((expit(z+b[0])+expit(z-b[0]))/2)
 standard.append(float(np.mean(vals)))
rows=[]
for j,n in enumerate(names):
 informative=(C[:,0,j]>0)^(C[:,1,j]>0)
 rows.append(dict(bot=n,appearances=int(A[:,j].sum()),matches=int((A[:,j]>0).sum()),wins=int(W[:,j].sum()),losses=int((A[:,j]-W[:,j]).sum()),rate=float(rates[j]),ci=ci[:,j].tolist(),bothSidesMatches=int(((C[:,0,j]>0)&(C[:,1,j]>0)).sum()),informativeRate=float(W[informative,j].sum()/A[informative,j].sum()),standardizedRate=standard[j]))
# Conditional teammate rates: two seat observations per team, as in the headline statistic.
partner=np.zeros((5,5,2),int);teams={};declarers={n:{} for n in names};deals=played=redeals=0;reconciled=0
for g in G:
 for s,n in enumerate(g['Lineup']):partner[names.index(n),names.index(g['Lineup'][(s+2)%4]),int(s%2==g['Winner'])]+=1
 for t in [0,1]:
  key='+'.join(sorted([g['Lineup'][t],g['Lineup'][t+2]],key=names.index));v=teams.setdefault(key,[0,0]);v[int(t==g['Winner'])]+=1
 with gzip.open(r/f"game-{g['Number']:03}.json.gz",'rt') as f:log=json.load(f)
 assert log['Winner']==g['Winner'] and log['Seed']==g['Seed'] and len(log['Rounds'])==g['Deals'];reconciled+=1
 for rd in log['Rounds']:
  deals+=1
  if rd['Redeal']:redeals+=1;continue
  played+=1;n=g['Lineup'][rd['Declarer']];kind=rd['Contract']['Kind'];v=declarers[n].setdefault(kind,[0,0]);v[0]+=1;v[1]+=int(rd['Made'])
blocks=[]
for start in range(0,N,250):blocks.append(dict(first=start+1,last=start+250,rates=(W[start:start+250].sum(0)/A[start:start+250].sum(0)).tolist()))
repeat=sum(len(set(g['Lineup']))<4 for g in G);same=sum(len(set(g['Lineup']))==1 for g in G)
result=dict(matches=N,rows=rows,bootstrapReplicates=B,team0Wins=int(y.sum()),repeatLineups=repeat,allSameLineups=same,blocks=blocks,model=dict(strengths=strength.tolist(),seatIntercept=float(b[0]),pairs=pairs,standardizedRates=standard,gradientNorm=float(np.linalg.norm(jac(b)))),partnerCounts=partner.tolist(),teamResults=teams,declarerContracts=declarers,deals=deals,played=played,redeals=redeals,reconciled=reconciled)
(out/'analysis.json').write_text(json.dumps(result,indent=2))
plt.rcParams.update({'font.size':11})
fig,ax=plt.subplots(figsize=(8,4.5));ax.errorbar(np.arange(5),100*rates,yerr=np.array([rates-ci[0],ci[1]-rates])*100,fmt='o',capsize=5,color='#2563eb');ax.axhline(50,color='gray',ls='--',lw=1);ax.set_xticks(range(5),names);ax.set_ylabel('Win rate (%)');ax.set_title('1,500 random-lineup matches: match-cluster bootstrap 95% intervals');ax.set_ylim(30,70);fig.tight_layout();fig.savefig(out/'win-rates.png',dpi=180);plt.close(fig)
fig,ax=plt.subplots(figsize=(8,4.5))
for j,n in enumerate(names):ax.plot([x['last'] for x in blocks],[x['rates'][j]*100 for x in blocks],marker='o',label=n)
ax.axhline(50,color='gray',ls='--',lw=1);ax.set_xlabel('Last match in non-overlapping 250-match block');ax.set_ylabel('Block win rate (%)');ax.legend(ncol=5,fontsize=9);fig.tight_layout();fig.savefig(out/'block-rates.png',dpi=180);plt.close(fig)
print(json.dumps({k:result[k] for k in ['rows','team0Wins','repeatLineups','allSameLineups','deals','played','redeals']}))
print('PAIRS',json.dumps(pairs))
