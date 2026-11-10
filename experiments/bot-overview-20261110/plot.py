"""Plot the complete nine-bot matched-state latency matrix; no match-outcome inference."""
import os
import tempfile
from pathlib import Path
import json
os.environ.setdefault('MPLCONFIGDIR',tempfile.mkdtemp(prefix='bot-overview-mpl-'))
os.environ.setdefault('XDG_CACHE_HOME',tempfile.mkdtemp(prefix='bot-overview-cache-'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import LogNorm
import numpy as np
folder=Path(__file__).resolve().parent
s=json.loads((folder/'summary.json').read_text())
profiles=list(s['Profiles'])
buckets=['bid','discard','early','middle','late']
values=np.array([[s['Performance'][b][p]['MedianMilliseconds'] for b in buckets] for p in profiles])
fig,ax=plt.subplots(figsize=(10,6.8),layout='constrained')
heat=ax.imshow(values,cmap='YlOrRd',norm=LogNorm(vmin=values.min(),vmax=values.max()),aspect='auto')
ax.set_xticks(range(5),['Bid','Discard','Play early','Play middle','Play late'])
ax.set_yticks(range(9),[s['Profiles'][p] for p in profiles])
for i in range(9):
    for j in range(5):
        v=values[i,j]
        ax.text(j,i,f'{v:.4f}' if v<.1 else f'{v:.2f}',ha='center',va='center',fontsize=10,color='white' if v>100 else 'black')
ax.set_title('Nine formal bots: matched-state decision time (ms)\n24 states per stage; median of per-state 3-repeat medians',pad=16)
fig.colorbar(heat,ax=ax,label='Decision time, ms (log scale)')
fig.savefig(folder/'performance.png',dpi=170)
