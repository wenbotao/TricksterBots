"""Static report figures; intervals preserve the two different experimental units."""
import json
from pathlib import Path
import os
import tempfile
cache=Path(tempfile.gettempdir())/'tricksterbots-comparison-plot'
cache.mkdir(parents=True,exist_ok=True)
os.environ.setdefault('MPLCONFIGDIR',str(cache))
os.environ.setdefault('XDG_CACHE_HOME',str(cache/'xdg'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

root=Path(__file__).resolve().parent;s=json.loads((root/'summary.json').read_text());assert s['Complete']
colors=['#237a9b','#d78038'];labels=['Incremental','StrongPlus']
fig,axes=plt.subplots(1,2,figsize=(9,3.8),layout='constrained')
rates=[];errors=[]
for arm in ['incremental','plus']:
    a=s['Replacement'][arm];rate=a['WinRate']*100;lo,hi=np.array(a['WinRateExact95'])*100
    rates.append(rate);errors.append([rate-lo,hi-rate])
axes[0].bar(labels,rates,color=colors,yerr=np.array(errors).T,capsize=5,width=.55)
axes[0].set_title('Replacement: 32 paired seed-roster blocks')
for i,arm in enumerate(['incremental','plus']):axes[0].text(i,rates[i]-9,f"{s['Replacement'][arm]['Wins']}/32",ha='center',color='white',weight='bold')
axes[0].set_ylabel('Team match win rate (%)');axes[0].set_ylim(0,105);axes[0].axhline(50,color='#999',lw=.7,ls='--')
axes[0].set_xlabel('Bars: observed rates; intervals: exact 95%')
d=s['Direct'];lo,hi=d['ClusterWinRate']['blockBootstrap95'];r=d['IncrementalWinRate']
axes[1].bar(labels,[100*r,100*(1-r)],color=colors,yerr=np.array([[100*(r-lo),100*(hi-r)],[100*(hi-r),100*(r-lo)]]).T,capsize=5,width=.55)
axes[1].set_title('Direct: 8 mirrored seed pairs (16 games)');axes[1].set_ylim(0,105);axes[1].axhline(50,color='#999',lw=.7,ls='--')
axes[1].set_xlabel('Intervals: seed-pair cluster bootstrap 95%')
for i,n in enumerate([d['IncrementalWins'],d['PlusWins']]):axes[1].text(i,100*r-9,f'{n}/16',ha='center',color='white',weight='bold')
fig.savefig(root/'outcomes.png',dpi=170);plt.close(fig)
keys=['natural-other-phase/bid/all','natural-other-phase/discard/all','natural-play-regression/play/early','natural-play-regression/play/middle','natural-play-regression/play/late']
names=['Bid (8)','Discard (8)','Play early (6)','Play middle (6)','Play late (6)'];x=np.arange(len(keys));width=.36
fig,axes=plt.subplots(1,2,figsize=(11,4.2),layout='constrained')
for idx,side in enumerate(['Incremental','Plus']):
    groups=[s['MatchedMicro']['Groups'][k][side] for k in keys]
    axes[0].bar(x+(idx-.5)*width,[g['Milliseconds'] for g in groups],width,label=labels[idx],color=colors[idx])
    axes[1].bar(x+(idx-.5)*width,[g['AllocatedBytes']/1e6 for g in groups],width,label=labels[idx],color=colors[idx])
for ax in axes:ax.set_xticks(x,names,rotation=25,ha='right');ax.set_yscale('log');ax.legend(frameon=False);ax.grid(axis='y',alpha=.2)
axes[0].set_ylabel('Median decision time (ms, log scale)');axes[1].set_ylabel('Median cumulative allocation (MB, log scale)')
fig.suptitle('Serial matched public states: two repeats per state; descriptive medians')
fig.savefig(root/'costs.png',dpi=170);plt.close(fig)
