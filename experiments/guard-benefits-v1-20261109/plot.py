"""Export the completed conditional discard estimates, independent of match progress."""
import json
from pathlib import Path
import os,tempfile
os.environ.setdefault('MPLCONFIGDIR',str(Path(tempfile.gettempdir())/'trickster-mpl-cache'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root=Path(__file__).resolve().parent
data=json.loads((root/'summary.json').read_text())['discard']['policyEffects']
fig,axes=plt.subplots(1,2,figsize=(10,3.8))
for ax,key,scale,title in zip(axes,['make','score'],[100,1],['Contract make-rate change (percentage points)','Relative score change (points)']):
    for y,policy,color in [(0,'medium','#2068ab'),(1,'weak','#ca7420')]:
        row=data[policy][key]
        mean=row['templateWeightedMeanDifference']*scale
        low,high=[x*scale for x in row['templateClusterBootstrap95']]
        ax.errorbar(mean,y,xerr=[[mean-low],[high-mean]],fmt='o',capsize=5,color=color,markersize=7)
        ax.text(mean,y-.18,f'{mean:+.2f}',ha='center',fontsize=10)
    ax.axvline(0,color='0.5',linestyle='--',linewidth=1)
    ax.set(yticks=[0,1],yticklabels=['Medium continuation','Weak continuation'],ylim=(1.5,-.5),xlabel=title)
    ax.grid(axis='x',alpha=.2)
    ax.spines[['top','right']].set_visible(False)
fig.suptitle('Conditional discard repair: 94 hand-template clusters',fontsize=13)
fig.text(.5,.015,'Forced 6NT; 128 changed decisions; independent hidden layouts. 95% hand-cluster bootstrap intervals.\nThese are conditional contract results, not match win-rate gains.',ha='center',fontsize=9)
fig.tight_layout(rect=(0,.13,1,.93))
fig.savefig(root/'conditional-discard-effects.png',dpi=180)
