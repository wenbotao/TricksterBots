"""Export a four-policy conditional-effect comparison."""
import os,tempfile,json
from pathlib import Path
os.environ.setdefault('MPLCONFIGDIR',str(Path(tempfile.gettempdir())/'trickster-mpl-cache'))
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

root=Path(__file__).resolve().parent;data=json.loads((root/'summary.json').read_text())['policies']
assert all(p['completeTemplateClusters']==94 for p in data.values())
fig,axes=plt.subplots(1,2,figsize=(10.5,5))
policies=['weak','medium','fast','strong'];colors=['#b77820','#2166ac','#24824d','#973552']
for ax,key,scale,title in zip(axes,['make','score'],[100,1],['Make-rate change (percentage points)','Relative score change (points)']):
    for y,(policy,color) in enumerate(zip(policies,colors)):
        row=data[policy][key];mean=row['mean']*scale;low,high=[x*scale for x in row['clusterBootstrap95']]
        ax.errorbar(mean,y,xerr=[[mean-low],[high-mean]],fmt='o',capsize=4,color=color)
        ax.text(mean,y-.18,f'{mean:+.2f}',ha='center',fontsize=9)
    ax.axvline(0,color='.5',linestyle='--',linewidth=1)
    ax.set(yticks=range(4),yticklabels=['Weak','Medium','Fast','Strong (original)'],ylim=(3.5,-.6),xlabel=title)
    ax.spines[['top','right']].set_visible(False);ax.grid(axis='x',alpha=.2)
fig.suptitle('Same repaired discards, different continuation policies',fontsize=13)
fig.text(.5,.02,'94 hand-template clusters; same 128 decisions and hidden layouts. 95% cluster bootstrap intervals.\nAll four seats use the indicated policy. Conditional 6NT results, not match win rates.',ha='center',fontsize=9)
fig.tight_layout(rect=(0,.12,1,.92));fig.savefig(root/'continuation-comparison.png',dpi=180)
