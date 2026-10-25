#!/usr/bin/env python3
"""Unified project workbench with explicit UI-draft and audio-mix save endpoints."""
from __future__ import annotations
import argparse,copy,hashlib,json,mimetypes,re,threading
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote,urlsplit
HERE=Path(__file__).resolve().parent
PROJECT=HERE.parents[1]
POLISH=PROJECT/'Assets/TripleMaze/Resources/Polish'
RUNTIME=PROJECT/'Assets/TripleMaze/Runtime/TripleMazeGame.cs'
DRAFT=PROJECT/'Docs/ui-preview-draft.json'
LOCK=threading.RLock()
CACHE={}
NUM=r'(-?\d+(?:\.\d+)?)(?:f)?'

def number(pattern,source,default,group=1):
    m=re.search(pattern,source,re.S)
    return float(m.group(group)) if m else default

def vec_after(source,marker,index=0):
    try:
        segment=source[source.index(marker):]
        matches=list(re.finditer(r'new Vector2\('+NUM+r','+NUM+r'\)',segment))
        m=matches[index]
        return float(m.group(1)),float(m.group(2))
    except (ValueError,IndexError):return None

def project_state():
    state=json.loads((HERE/'defaults.json').read_text())
    code=RUNTIME.read_text()
    approved=json.loads((PROJECT/'Docs/ui-layout-approved.json').read_text())
    for section in ['game','menu','feedback','result']:
        state[section].update({k:v for k,v in approved.get(section,{}).items() if k in state[section]})
    elements=copy.deepcopy(approved.get('elements',{}))
    for key in ['resultGlow','menuSound','gameBack','gameRetry']:elements.pop(key,None)
    state['elements']=elements
    home=code[code.index('public void Home()'):code.index('static ArrowData A(')]
    game=code[code.index('public void StartLevel(LevelData data)'):code.index('float TrayStep')]
    result=code[code.index('void Result()'):code.index('IEnumerator ResultFireworks')]
    popup=code[code.index('void OpenSettings()'):code.index('void VolumeSlider(')]
    xy=vec_after(home,'Icon("title-art"')
    size=vec_after(home,'Icon("title-art"',1)
    if xy:state['menu']['logoY']=640-xy[1]
    if size:state['menu']['logoWidth']=size[0]
    size=vec_after(home,'Button($"开始第',1)
    if size:state['menu']['buttonWidth']=size[0]
    xy=vec_after(home,'Button($"开始第')
    if xy:state['menu']['buttonY']=640-xy[1]
    size=vec_after(game,'Rect("Hint button layout"',1)
    if size:state['game']['toolsWidth']=size[0]
    xy=vec_after(game,'Rect("Hint button layout"')
    if xy:state['game']['toolsY']=640-xy[1]
    for label,key in [('Hint','gameHint'),('Undo','gameUndo')]:
        scale=number(r'var '+label.lower()+r'Root=.*?localScale=Vector3.one\*'+NUM,game,.8)
        elements.setdefault(key,dict(visible=True,x=0,y=0,scale=100))['scale']=round(scale*100,2)
    life=vec_after(game,'Rect("Remaining stars"')
    if life:elements['gameLife']=dict(visible=True,x=life[0]+239,y=550-life[1],scale=100)
    elements['gameProgress']=dict(visible='track.gameObject.SetActive(false)' not in game,x=0,y=0,scale=100)
    state['feedback']['width']=number(r'feedbackWidth='+NUM,code,239)
    state['feedback']['y']=640-number(r'const float origin='+NUM,code,465)
    title=re.search(r'Label\(win\?"([^"]*)":"([^"]*)",screen,new Vector2\('+NUM+','+NUM+r'\),new Vector2\('+NUM+','+NUM+r'\),'+NUM, result)
    if title:
        state['texts']['winTitle'],state['texts']['loseTitle']=title.group(1),title.group(2)
        state['result']['titleY']=640-float(title.group(4));state['result']['titleSize']=float(title.group(7))
    xy=vec_after(result,'Icon(win?"crown":"hint"')
    if xy:state['result']['crownY']=640-xy[1]
    size=vec_after(result,'Icon(win?"crown":"hint"',1)
    if size:state['result']['crownWidth']=size[0]
    size=vec_after(result,'Icon(win?"crown":"hint"',2)
    if size:state['result']['bulbWidth']=size[0]
    star=re.search(r'new Vector2\(\(n-1\)\*104,n==1\?'+NUM+':'+NUM+r'\)',result)
    if star:state['result']['starsY']=640-float(star.group(1))-elements.get('resultStars',{}).get('y',0)
    size=vec_after(result,'Button(win?',1)
    if size:state['result']['buttonWidth']=size[0]
    xy=vec_after(result,'Button(win?')
    if xy:state['result']['buttonsY']=640-xy[1]
    elements['resultStats']=dict(visible='Box("Result stats"' in result,x=0,y=0,scale=100)
    levelScale=number(r'Label\(\$"LEVEL.*?localScale=Vector3.one\*'+NUM,result,2)
    elements['resultLevel']=dict(visible=True,x=0,y=0,scale=levelScale*100)
    size=vec_after(popup,'Box("Settings panel"')
    if size:state['settings']['width'],state['settings']['height']=size
    patterns={
        'settings':r'Button\("([^"\n]*)",screen,new Vector2\(292,550\)',
        'hint':r'Button\("([^"\n]*)",hintRoot',
        'undo':r'Button\("([^"\n]*)",undoRoot',
        'resultHome':r'Button\("([^"\n]*)",screen,new Vector2\(0,-425\)',
        'settingsTitle':r'Label\("([^"\n]*)",panelRoot,new Vector2\(0,285\)',
        'music':r'VolumeSlider\(panelRoot,"([^"\n]*)",145',
        'effects':r'VolumeSlider\(panelRoot,"([^"\n]*)",15',
        'volumeHint':r'Label\("([^"\n]*)",panelRoot,new Vector2\(0,-75\)',
        'restart':r'Button\("([^"\n]*)",panelRoot,new Vector2\(0,-150\)',
        'home':r'Button\("([^"\n]*)",panelRoot,new Vector2\(0,-235\)',
        'close':r'Button\("([^"\n]*)",panelRoot,new Vector2\(0,-315\)',
    }
    for key,pattern in patterns.items():
        match=re.search(pattern,code)
        if match:state['texts'][key]=match.group(1)
    nextMatch=re.search(r'Button\(win\?"([^"]*)":"([^"]*)"',result)
    if nextMatch:state['texts']['next'],state['texts']['retry']=nextMatch.groups()
    for key,pattern in [('start',r'Button\(\$"(开始第[^"]*)"'),('gameTitle',r'Label\(\$"(第[^"]*)"'),('level',r'Label\(\$"(LEVEL[^"]*)"')]:
        match=re.search(pattern,code)
        if match:state['texts'][key]=re.sub(r'\{(?:saved|data\.levelId)(:00)?\}',lambda m:'{level:00}' if m.group(1) else '{level}',match.group(1))
    return state

def revision():
    files=list((PROJECT/'Assets/TripleMaze/Runtime').glob('*.cs'))+list(POLISH.glob('*'))+[PROJECT/'Docs/ui-layout-approved.json',PROJECT/'Assets/TripleMaze/Resources/Levels/level_06.json']+list(HERE.glob('*.js'))+list(HERE.glob('*.css'))+list(HERE.glob('*.html'))+[HERE/'defaults.json']
    digest=hashlib.sha256()
    for path in sorted(files):
        if path.is_file():
            stat=path.stat();digest.update(f'{path.name}:{stat.st_mtime_ns}:{stat.st_size}'.encode())
    return digest.hexdigest()[:20]

def build_page():
    rev=revision()
    with LOCK:
        if CACHE.get('revision')==rev:return CACHE['page']
        source=(HERE/'preview-fragment.html').read_text()
        state=project_state()
        source=re.sub(r'const defaults=.*?;\nconst elementSpecs=',lambda m:'const defaults='+json.dumps({k:v for k,v in state.items() if k!='elements'},ensure_ascii=False)+';\nconst elementSpecs=',source,count=1)
        source=source.replace('const state=JSON.parse(JSON.stringify(defaults));','Object.assign(defaults.elements,'+json.dumps(state['elements'],ensure_ascii=False)+');\nconst state=JSON.parse(JSON.stringify(defaults));')
        source=source.replace('restore(window.openai?.widgetState);','')
        level=PROJECT/'Assets/TripleMaze/Resources/Levels/level_06.json'
        if level.exists():source=re.sub(r'(<script id="tm-level-data" type="application/json">).*?(</script>)',lambda m:m.group(1)+json.dumps(json.loads(level.read_text()))+m.group(2),source,count=1,flags=re.S)
        # Browser drafts are deliberately separate from project defaults.
        assets_match=re.search(r'(<script id="tm-art-data" type="application/json">)(.*?)(</script>)',source,re.S)
        assets=json.loads(assets_match.group(2))
        for key in list(assets):
            stem='fireworks' if key=='fireworks-audio' else key
            match=next((p for p in POLISH.glob(stem+'.*') if p.suffix in {'.png','.wav','.mp3'}),None)
            if match:assets[key]='/assets/'+match.name+'?v='+rev
        assets['ambience']='/assets/ambience.wav?v='+rev
        mix=POLISH/'AudioMix.json'
        if mix.exists():assets['mix']={x['key']:x.get('gain',1) for x in json.loads(mix.read_text()).get('clips',[])}
        source=source[:assets_match.start(2)]+json.dumps(assets)+source[assets_match.end(2):]
        source=source.replace('state.audio.effects/100*.7;','state.audio.effects/100*.7*(assets.mix?.[key===\'fireworks-audio\'?\'fireworks\':key]??1);',1)
        scripts=list(re.finditer(r'<script(?:\s[^>]*)?>(.*?)</script>',source,re.S))
        main=scripts[-1];javascript=main.group(1);source=source[:main.start()]+source[main.end():]
        shell=(HERE/'local-shell.html').read_text().replace('<html lang="zh-CN">',f'<html lang="zh-CN" data-revision="{rev}">')
        shell=shell.replace('__FRAGMENT__',source).replace('__AUDIO_FRAGMENT__',(HERE/'audio-fragment.html').read_text())
        bootstrap='<script>'+(HERE/'native-tweak.js').read_text()+'</script><script>'+javascript+'</script>'
        shell=shell.replace('<script src="/workbench.js"></script>',bootstrap+'<script src="/workbench.js"></script>')
        CACHE.update(revision=rev,page=shell.encode())
        return CACHE['page']

class Handler(BaseHTTPRequestHandler):
    def send(self,data,kind='application/json',status=200):
        if isinstance(data,str):data=data.encode()
        self.send_response(status);self.send_header('Content-Type',kind+'; charset=utf-8' if kind.startswith(('text/','application/json')) else kind);self.send_header('Cache-Control','no-store');self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
    def do_GET(self):
        path=unquote(urlsplit(self.path).path)
        try:
            if path in {'/','/index.html'}:return self.send(build_page(),'text/html')
            if path=='/api/status':return self.send(json.dumps(dict(revision=revision())))
            if path=='/api/audio-mix':return self.send((POLISH/'AudioMix.json').read_bytes())
            if path=='/api/draft':return self.send(DRAFT.read_bytes()) if DRAFT.exists() else self.send('{}',status=404)
            if path=='/api/project':return self.send(json.dumps(project_state(),ensure_ascii=False))
            if path.startswith('/assets/'):
                name=path[len('/assets/'):];target=(POLISH/name).resolve()
                if target.parent!=POLISH.resolve() or target.suffix not in {'.png','.wav','.mp3','.ttf'}:return self.send('{}',status=404)
                return self.send(target.read_bytes(),mimetypes.guess_type(str(target))[0] or 'application/octet-stream')
            if path in {'/workbench.css','/workbench.js','/audio.css','/audio.js'}:return self.send((HERE/path[1:]).read_bytes(),'text/css' if path.endswith('.css') else 'text/javascript')
            return self.send('{}',status=404)
        except Exception as error:return self.send(json.dumps(dict(error=str(error)),ensure_ascii=False),status=500)
    def do_POST(self):
        if self.path not in {'/api/draft','/api/audio-mix'}:return self.send('{}',status=404)
        origin=self.headers.get('Origin')
        if origin and origin!='http://'+self.headers.get('Host',''):return self.send('{}',status=403)
        try:
            length=int(self.headers.get('Content-Length','0'))
            if length<1 or length>65536:raise ValueError('参数大小不正确')
            data=json.loads(self.rfile.read(length))
            if self.path=='/api/audio-mix':
                clips=data.get('clips')
                if not isinstance(clips,list) or len(clips)>64:raise ValueError('音频配置格式错误')
                entries=[];seen=set()
                for entry in clips:
                    key=entry.get('key');gain=entry.get('gain')
                    if not isinstance(key,str) or not isinstance(gain,(int,float)) or isinstance(gain,bool) or not 0<=gain<=1:raise ValueError('音频比例应在 0 到 1 之间')
                    if key in seen or not any((POLISH/(key+ext)).exists() for ext in ['.wav','.mp3']):raise ValueError('未知或重复的音频资源')
                    seen.add(key);entries.append(dict(key=key,gain=gain))
                target=POLISH/'AudioMix.json';target.with_suffix('.json.tmp').write_text(json.dumps(dict(clips=entries),ensure_ascii=False,indent=2)+'\n');target.with_suffix('.json.tmp').replace(target)
                return self.send(json.dumps(dict(saved=str(target)),ensure_ascii=False))
            if data.get('version')!=3:raise ValueError('不支持的参数版本')
            DRAFT.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
            self.send(json.dumps(dict(saved=str(DRAFT)),ensure_ascii=False))
        except Exception as error:self.send(json.dumps(dict(error=str(error)),ensure_ascii=False),status=400)
    def log_message(self,*args):pass

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--port',type=int,default=8877);parser.add_argument('--open',action='store_true');args=parser.parse_args()
    server=ThreadingHTTPServer(('127.0.0.1',args.port),Handler)
    print(f'Triple Maze: http://127.0.0.1:{args.port}',flush=True)
    if args.open:
        import webbrowser
        threading.Timer(.3,lambda:webbrowser.open(f'http://127.0.0.1:{args.port}/')).start()
    try:server.serve_forever()
    except KeyboardInterrupt:pass
    finally:server.server_close()
if __name__=='__main__':main()
