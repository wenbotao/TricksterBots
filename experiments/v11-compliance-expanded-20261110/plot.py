"""Export the primary study's rates and match-cluster confidence intervals."""
from pathlib import Path
import json
import os
import tempfile

os.environ.setdefault("MPLCONFIGDIR", str(Path(tempfile.gettempdir()) / "fivehundred-mpl"))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

HERE = Path(__file__).resolve().parent
data = json.loads((HERE / "summary.json").read_text())["Primary540"]["Wins"]
labels = {"random-v1.1": "Random", "random-pass-v1.1": "RandomPass", "weak-v1.1": "Weak",
          "medium-v1.1": "Medium", "fast-v1.1": "Fast", "strong-v1.1": "Strong", "partner-v1.1": "Partner",
          "strong-plus-v1.1": "StrongPlus", "strong-flow-cached-v1.1": "StrongFlowCached"}
keys = sorted(data, key=lambda p: data[p]["WinRate"])
rates = np.array([data[p]["WinRate"] * 100 for p in keys])
low = np.array([data[p]["ClusterBootstrap95"][0] * 100 for p in keys])
high = np.array([data[p]["ClusterBootstrap95"][1] * 100 for p in keys])
fig, ax = plt.subplots(figsize=(9, 5.5), layout="constrained")
ax.barh([labels[p] for p in keys], rates, color="#4477aa", alpha=.83)
ax.errorbar(rates, np.arange(len(keys)), xerr=[rates-low, high-rates], fmt="none", ecolor="#223344", capsize=4)
for y, p in enumerate(keys):
    ax.text(99, y, f"{data[p]['Wins']}/240", va="center", ha="right", fontsize=9)
ax.axvline(50, color="#aa4444", linestyle="--", linewidth=1)
ax.set(xlim=(0, 100), xlabel="Seat-appearance win rate (%) · match-cluster bootstrap 95% CI",
       title="Five Hundred v1.1 · Australian default · 540 independent random matches")
ax.grid(axis="x", alpha=.15)
ax.set_axisbelow(True)
fig.savefig(HERE / "win-rates.png", dpi=180)
fig.savefig(HERE / "win-rates.svg")
# Matplotlib emits trailing spaces in multiline path coordinates. Newlines still
# separate SVG tokens, so normalize the text for clean repository diffs.
svg = HERE / "win-rates.svg"
svg.write_text("\n".join(line.rstrip() for line in svg.read_text().splitlines()) + "\n")
plt.close(fig)
