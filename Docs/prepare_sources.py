from pathlib import Path
import yaml,json,random,shutil
root=Path(__file__).resolve().parents[1];src=Path('/Users/julienren/unity-arrow-smash/ArrowSmash/Assets/ArrowSmash/Bundles/LevelsHot');out=root/'Assets/TripleMaze/Resources'; records=[]
def cells(a):
 for p,q in zip(a['points'],a['points'][1:]):
  dx=q['x']-p['x'];dy=q['y']-p['y'];n=max(abs(dx),abs(dy))
  if not n or dx and dy: raise ValueError()
  for t in range(n):yield(p['x']+t*((dx>0)-(dx<0)),p['y']+t*((dy>0)-(dy<0)))
 yield(a['points'][-1]['x'],a['points'][-1]['y'])
candidates=[]
for p in src.glob('*.asset'):
 try:
  s=p.read_text();d=yaml.safe_load(s[s.index('MonoBehaviour:'):])['MonoBehaviour']['levelConfig'];arr=d['initialSegments'];n=len(arr)
  if not 9<=n<=60:continue
  if any(d.get(k) for k in ['initialLocks','initialArrowBundles','initialPipes','initialCorners','initialElevators']):continue
  occupied=set();cs=[]
  for a in arr:
   cc=set(cells(a))
   if len(cc)!=len(list(cells(a))) or cc&occupied:raise ValueError()
   occupied|=cc;cs.append(cc)
  remaining=set(range(n));order=[]
  while remaining:
   available=[]
   for i in sorted(remaining):
    a=arr[i];p0,p1=a['points'][:2];dx=(p0['x']>p1['x'])-(p0['x']<p1['x']);dy=(p0['y']>p1['y'])-(p0['y']<p1['y']);x,y=p0['x']+dx,p0['y']+dy;blocks=set().union(*(cs[j] for j in remaining if j!=i))
    while 0<=x<d['gridWidth'] and 0<=y<d['gridHeight']:
     if (x,y) in blocks:break
     x+=dx;y+=dy
    else:available.append(i)
   if not available:raise ValueError()
   i=available[0];remaining.remove(i);order.append(i)
  candidates.append((n,p.name,d,order))
 except (ValueError,KeyError,TypeError):pass
candidates.sort(key=lambda x:(x[0],x[1])); chosen=[];seen=set()
for n,name,d,order in candidates:
 sig=json.dumps([a['points'] for a in d['initialSegments']],sort_keys=True)
 if sig in seen:continue
 seen.add(sig);chosen.append((n,name,d,order))
 if len(chosen)==20:break
assert len(chosen)==20,len(chosen)
for index,(n,name,d,order) in enumerate(chosen,1):
 # 删除已可移出的少量箭头，使每色总数都能三消；其余地图沿用原项目。
 order=order[n%3:];arr=[{'points':a['points'],'colorDisplayIndex':1} for a in d['initialSegments']];rng=random.Random(410+index)
 sequence=[]
 for g in range(len(order)//3):sequence.extend([g%min(2+index//5,5)+1]*3)
 # 在每个六枚窗口内打散两组三消，保证给定解的槽位峰值不超过五。
 for start in range(0,len(sequence),6):
  part=sequence[start:start+6];rng.shuffle(part);sequence[start:start+6]=part
 for i,c in zip(order,sequence):arr[i]['colorDisplayIndex']=c
 keep=sorted(order);mapping={old:new for new,old in enumerate(keep)}
 data={'levelId':index,'description':['初识三色','留出空间','交错路径','颜色规划'][min(3,(index-1)//5)],'gridWidth':d['gridWidth'],'gridHeight':d['gridHeight'],'slotCapacity':7,'initialSegments':[arr[i] for i in keep]}
 (out/'Levels'/f'level_{index:02}.json').write_text(json.dumps(data,ensure_ascii=False,indent=2))
 records.append({'level':index,'source':name,'sourceArrowCount':n,'arrowCount':len(keep),'knownSolution':[mapping[i] for i in order]})
analysis=root.parent/'amaze-go-analysis';assets=json.loads((analysis/'asset-index.json').read_text());copied=[]
for name in ['CommonBtnBig','CommonDialogBg','close_black','icon_time_no_pointer']:
 row=next((x for x in assets if x.get('name')==name and x['type']=='Sprite'),None)
 if row:shutil.copy2(analysis/row['output'],out/'UI'/f'{name}.png');copied.append(row)
for target,source in [('click','common_click'),('exit','arrow_out_soft'),('match','combo_sfx1'),('blocked','pipe_collision')]:
 row=next((x for x in assets if x['type']=='AudioClip' and x.get('name')==source and x.get('outputs')),None)
 if row:shutil.copy2(analysis/row['outputs'][0],out/'Audio'/f'{target}.wav');copied.append(row)
(root/'Docs/source-levels.json').write_text(json.dumps(records,ensure_ascii=False,indent=2));(root/'Docs/amaze-assets.json').write_text(json.dumps(copied,ensure_ascii=False,indent=2));print('Created',len(records),'levels;',len(copied),'Amaze UI/audio assets')
