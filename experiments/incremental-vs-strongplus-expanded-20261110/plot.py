"""Replication and cost figures with their independent sampling units preserved."""
import json,os,tempfile
from pathlib import Path
root=Path(__file__).resolve().parent
cache=Path(tempfile.gettempdir())/'tricksterbots-expanded-plots';cache.mkdir(exist_ok=True)
os.environ.setdefault('MPLCONFIGDIR',str(cache));os.environ.setdefault('XDG_CACHE_HOME',str(cache/'xdg'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
s=json.loads((root/'summary.json').read_text());old=json.loads((root.parent/'incremental-vs-strongplus-20261109/summary.json').read_text());assert s['Complete']
fig,axes=plt.subplots(1,2,figsize=(11,4),layout='constrained')
rep=[old['ReplacementPaired'],s['ReplacementPaired'],s['PooledAuxiliary']['Replacement']]
direct=[old['Direct'],s['Direct'],s['PooledAuxiliary']['Direct']]
labels=['Prior (auxiliary)','New (primary)','Pooled (auxiliary)']
for i,(r,d) in enumerate(zip(rep,direct)):
    point=100*r['WinDifference'];lo,hi=np.array(r['PairedWinBootstrap']['blockBootstrap95'])*100
    axes[0].errorbar(point,i,xerr=[[point-lo],[hi-point]],fmt='o',color='#237a9b',capsize=4)
    value=d['ClusterWinRate'];point=100*value['mean'];lo,hi=np.array(value['blockBootstrap95'])*100
    axes[1].errorbar(point,i,xerr=[[point-lo],[hi-point]],fmt='o',color='#a76123',capsize=4)
for ax in axes:ax.set_yticks(range(3),labels);ax.invert_yaxis();ax.grid(axis='x',alpha=.2)
axes[0].axvline(0,color='gray',ls='--');axes[0].set_xlabel('Current - StrongPlus team win rate (percentage points)');axes[0].set_title('Replacement pairs: 32 / 128 / 160')
axes[1].axvline(50,color='gray',ls='--');axes[1].set_xlabel('Current mirrored match win rate (%)');axes[1].set_title('Direct seed pairs: 8 / 32 / 40')
fig.suptitle('95% block bootstrap intervals; new batch is the primary evidence')
fig.savefig(root/'outcomes.png',dpi=170);plt.close(fig)
keys=['bid','discard','early','middle','late'];labels=['Bid','Discard','Play early','Play middle','Play late'];fig,axes=plt.subplots(1,2,figsize=(10,4.3),layout='constrained')
for col,(metric,title) in enumerate([('TimeRatio','Decision time'),('AllocationRatio','Cumulative allocation')]):
    for i,key in enumerate(keys):
        d=s['MatchedMicro']['Groups'][key][metric];point=d['GeometricMeanBySourceBlock'];lo,hi=d['SourceBlockBootstrap95']
        axes[col].errorbar(point,i,xerr=[[point-lo],[hi-point]],fmt='o',capsize=4,color='#237a9b' if point<1 else '#d78038')
    axes[col].axvline(1,color='gray',ls='--');axes[col].set_yticks(range(5),labels);axes[col].invert_yaxis();axes[col].set_xscale('log');axes[col].grid(axis='x',alpha=.2)
    axes[col].set_xlabel('Geometric current / StrongPlus ratio (log scale)');axes[col].set_title(title)
fig.suptitle('New 144-state corpus: 24 per stratum, source-block bootstrap 95%')
fig.savefig(root/'costs.png',dpi=170);plt.close(fig)
