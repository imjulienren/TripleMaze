"""从 ArrowSmash 的普通地图编排渐进关卡，保存确定可重放的解。"""
from pathlib import Path
import json,yaml,random
root=Path(__file__).resolve().parents[1];src=Path('/Users/julienren/unity-arrow-smash/ArrowSmash/Assets/ArrowSmash/Bundles');candidates=[]
def cells(a):
 out=[]
 for p,q in zip(a['points'],a['points'][1:]):
  dx=q['x']-p['x'];dy=q['y']-p['y'];n=max(abs(dx),abs(dy))
  if n==0 or dx and dy:raise ValueError()
  out.extend((p['x']+i*((dx>0)-(dx<0)),p['y']+i*((dy>0)-(dy<0))) for i in range(n))
 out.append((a['points'][-1]['x'],a['points'][-1]['y']));return out
for group in ['Levels','LevelsHot']:
 for path in (src/group).glob('*.asset'):
  try:
   text=path.read_text();d=yaml.safe_load(text[text.index('MonoBehaviour:'):])['MonoBehaviour']['levelConfig'];arr=d['initialSegments'];n=len(arr)
   if not 3<=n<=65:continue
   if any(d.get(k) for k in ['initialLocks','initialArrowBundles','initialPipes','initialCorners','initialElevators']):continue
   cs=[];allpoints=set()
   for a in arr:
    pts=cells(a);cc=set(pts)
    if len(pts)!=len(cc) or cc&allpoints:raise ValueError()
    if any(x<0 or y<0 or x>=d['gridWidth'] or y>=d['gridHeight'] for x,y in cc):raise ValueError()
    allpoints|=cc;cs.append(cc)
   remaining=set(range(n));order=[]
   while remaining:
    free=[]
    for i in sorted(remaining):
     a,b=arr[i]['points'][:2];dx=(a['x']>b['x'])-(a['x']<b['x']);dy=(a['y']>b['y'])-(a['y']<b['y']);x,y=a['x']+dx,a['y']+dy;block=set().union(*(cs[j] for j in remaining if j!=i))
     while 0<=x<d['gridWidth'] and 0<=y<d['gridHeight']:
      if (x,y) in block:break
      x+=dx;y+=dy
     else:free.append(i)
    if not free:raise ValueError()
    i=free[0];remaining.remove(i);order.append(i)
   signature=json.dumps([a['points'] for a in arr],sort_keys=True)
   candidates.append({'n':n,'path':str(path.relative_to(src)),'data':d,'order':order,'signature':signature})
  except (ValueError,KeyError,TypeError):pass
records=[];seen=set();desired=[3,6,9,12,15,18,21,24,24,27,30,33,36,36,39,42,42,45,48,51]
titles=['第一次相遇','再收一组','循着颜色','留一格余地','交错的出口','先后之间','绕个弯遇见你','不急，刚刚好','四种心情','给颜色让路','多看一步','藏起来的出口','慢慢解开','留给下一组','弯弯绕绕','颜色的秩序','先出发，再相遇','一格的选择','最后的交错','所有颜色都归位']
for idx,target in enumerate(desired,1):
 usable=[c for c in candidates if c['n']>=target and c['signature'] not in seen]
 c=min(usable,key=lambda c:(c['n']-target,0 if c['path'].startswith('Levels/') else 1,c['path']));seen.add(c['signature']);d=c['data'];order=c['order'][c['n']-target:];rng=random.Random(6200+idx)
 colors=min(target//3,1 if idx==1 else 2 if idx==2 else 3 if idx==3 else 4 if idx<11 else 5)
 seq=[1+(g%colors) for g in range(target//3) for k in range(3)]
 # 分波打乱，同一解的槽位峰值限定在六枚；后期引入四色和五色，才真正产生七格空间压力。
 wave=colors*3
 for start in range(0,len(seq),wave):
  part=seq[start:start+wave]
  for trial in range(100):
   rng.shuffle(part);counts={};peak=0
   for x in part:
    counts[x]=counts.get(x,0)+1
    if counts[x]==3:counts[x]=0
    peak=max(peak,sum(counts.values()))
   if peak<=6:break
  else:part=sorted(part)
  seq[start:start+wave]=part
 arr=[{'points':a['points'],'colorDisplayIndex':1} for a in d['initialSegments']]
 for i,color in zip(order,seq):arr[i]['colorDisplayIndex']=color
 keep=sorted(order);mapping={old:new for new,old in enumerate(keep)}
 level={'levelId':idx,'description':titles[idx-1],'gridWidth':d['gridWidth'],'gridHeight':d['gridHeight'],'slotCapacity':7,'initialSegments':[arr[i] for i in keep]}
 (root/'Assets/TripleMaze/Resources/Levels'/f'level_{idx:02}.json').write_text(json.dumps(level,ensure_ascii=False,indent=2))
 records.append({'level':idx,'source':c['path'],'sourceArrowCount':c['n'],'arrowCount':target,'colorCount':len(set(seq)),'knownSolution':[mapping[i] for i in order]})
(root/'Docs/source-levels.json').write_text(json.dumps(records,ensure_ascii=False,indent=2));print([(r['level'],r['arrowCount'],r['colorCount'],r['source']) for r in records])
