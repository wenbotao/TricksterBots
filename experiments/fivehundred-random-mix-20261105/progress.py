import json,time
from pathlib import Path
r=Path(__file__).parent
games=[]
for p in sorted(r.glob('game-*.json')):
 try: games.append(json.loads(p.read_text()))
 except json.JSONDecodeError: pass
new=[g for g in games if g['Number']>750]
print('New:',len(new),'Total:',len(games),'Errors:',sum(g['Error'] is not None for g in games),'Truncated:',sum(g['Truncated'] for g in games))
for name in ['Weak','Medium','Fast','Strong','Partner']:
 def count(gs):
  w=l=0
  for g in gs:
   if g['Error'] is not None or g['Truncated'] or g['Winner'] is None:continue
   for s,n in enumerate(g['Lineup']):
    if n==name:
     if s%2==g['Winner']:w+=1
     else:l+=1
  return w,l
 w,l=count(new);a,b=count(games)
 print(name,f'new {w}/{l} {w/(w+l):.1%}' if w+l else '-',f'total {a}/{b} {a/(a+b):.1%}')
