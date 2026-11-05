import json, sys
from pathlib import Path
root = Path(__file__).parent
through = int(sys.argv[1])
s = json.loads((root / f'summary-{through:03}.json').read_text())
for label in ['Batch', 'Cumulative']:
 data = s[label]
 assert sum(b['Appearances'] for b in data['Bots']) == data['Matches'] * 4
 assert sum(b['Wins'] for b in data['Bots']) == data['Completed'] * 2
 assert sum(b['Losses'] for b in data['Bots']) == data['Completed'] * 2
 print(label, 'matches', data['Matches'], 'completed', data['Completed'], 'truncated', data['Truncated'], 'errors', data['Errors'])
 for b in data['Bots']:
  print(b['Bot'],b['Appearances'],b['Wins'],b['Losses'],f"{b['WinRate']:.1%}" if b['WinRate'] is not None else '-')
