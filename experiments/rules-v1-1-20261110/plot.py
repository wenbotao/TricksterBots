import os,tempfile,json
from pathlib import Path
os.environ.setdefault('MPLCONFIGDIR',tempfile.mkdtemp(prefix='v11-mpl-'))
os.environ.setdefault('XDG_CACHE_HOME',tempfile.mkdtemp(prefix='v11-cache-'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import LogNorm
import numpy as np
folder=Path(__file__).resolve().parent;s=json.loads((folder/'summary.json').read_text())
profiles=list(s['Profiles'])
fig,ax=plt.subplots(figsize=(9,5.5),layout='constrained')
y=np.arange(9);rates=np.array([s['Wins'][p]['WinRate'] for p in profiles]);intervals=np.array([s['Wins'][p]['MatchBootstrap95'] for p in profiles])
ax.errorbar(rates*100,y,xerr=np.array([rates-intervals[:,0],intervals[:,1]-rates])*100,fmt='o',capsize=4)
ax.set_yticks(y,[s['Profiles'][p] for p in profiles]);ax.invert_yaxis();ax.axvline(50,color='grey',ls='--',alpha=.5)
ax.set_xlabel('Seat win rate (%) with match-cluster bootstrap 95% interval');ax.set_xlim(0,100)
ax.set_title('New Australian v1.1 default: 180 fixed random lineups\n80 seat appearances per bot; observations within a match are correlated')
fig.savefig(folder/'win-rates.png',dpi=170)
buckets=['bid','discard','early','middle','late'];data=np.array([[s['Costs'][b][p]['MillisecondsMedian'] for b in buckets] for p in profiles])
fig,ax=plt.subplots(figsize=(10.5,6.4),layout='constrained')
img=ax.imshow(data,norm=LogNorm(vmin=data[data>0].min(),vmax=data.max()),cmap='YlOrRd',aspect='auto')
ax.set_yticks(y,[s['Profiles'][p] for p in profiles]);ax.set_xticks(range(5),['Bid','Discard','Early','Middle','Late'])
for i in range(9):
 for j in range(5):
  v=data[i,j];ax.text(j,i,f'{v:.3f}' if v<.1 else f'{v:.1f}',ha='center',va='center',color='white' if v>100 else 'black')
ax.set_title('New-rule matched-state latency (ms)\n12 states per stage, median of per-state 3-repeat medians')
fig.colorbar(img,ax=ax,label='Decision time, ms (log scale)');fig.savefig(folder/'costs.png',dpi=170)
