using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TripleMaze
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SoftPanel : MaskableGraphic
    {
        public float Radius=24;
        public Color Bottom=Color.white;
        public bool Gradient;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;float rad=Mathf.Min(Radius,Mathf.Min(r.width,r.height)*.5f);
            Vector2 center=r.center;int n=48;
            for(int i=0;i<n;i++)
            {
                Vector2 a=Edge(i,rad,r),b=Edge((i+1)%n,rad,r);
                Tri(vh,center,a,b,Shade(center,r),Shade(a,r),Shade(b,r));
                Vector2 oa=a+(a-center).normalized*1.1f,ob=b+(b-center).normalized*1.1f;
                var ca=Shade(a,r);var cb=Shade(b,r);var za=ca;za.a=0;var zb=cb;zb.a=0;
                int k=vh.currentVertCount;vh.AddVert(a,ca,Vector2.zero);vh.AddVert(b,cb,Vector2.zero);vh.AddVert(ob,zb,Vector2.zero);vh.AddVert(oa,za,Vector2.zero);vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
            }
        }
        Color Shade(Vector2 p,Rect r)=>Gradient?Color.Lerp(Bottom,color,Mathf.InverseLerp(r.yMin,r.yMax,p.y)):color;
        static Vector2 Edge(int i,float rad,Rect r)
        {int q=i/12;float angle=(q*90+(i%12)*90f/11)*Mathf.Deg2Rad;Vector2 c=new Vector2(q==0||q==3?r.xMax-rad:r.xMin+rad,q<2?r.yMax-rad:r.yMin+rad);return c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*rad;}
        static void Tri(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color ca,Color cb,Color cc)
        {int n=v.currentVertCount;v.AddVert(a,ca,Vector2.zero);v.AddVert(b,cb,Vector2.zero);v.AddVert(c,cc,Vector2.zero);v.AddTriangle(n,n+1,n+2);}
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SoftGlow : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper v)
        {v.Clear();var r=rectTransform.rect;var clear=color;clear.a=0;for(int i=0;i<48;i++){int n=v.currentVertCount;float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;v.AddVert(r.center,color,Vector2.zero);v.AddVert(r.center+Vector2.Scale(new Vector2(Mathf.Cos(a),Mathf.Sin(a)),r.size*.5f),clear,Vector2.zero);v.AddVert(r.center+Vector2.Scale(new Vector2(Mathf.Cos(b),Mathf.Sin(b)),r.size*.5f),clear,Vector2.zero);v.AddTriangle(n,n+1,n+2);}}
    }
    public sealed class PressFeedback : MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IPointerExitHandler
    {
        Vector3 original;Coroutine motion;
        void Awake(){original=transform.localScale;}
        void OnDisable(){if(motion!=null)StopCoroutine(motion);transform.localScale=original;}
        public void OnPointerDown(PointerEventData e){var b=GetComponent<Button>();if(b && b.interactable)Animate(.94f,.055f,false);}
        public void OnPointerUp(PointerEventData e){Animate(1,.18f,true);}
        public void OnPointerExit(PointerEventData e){Animate(1,.15f,false);}
        void Animate(float target,float duration,bool bounce){if(motion!=null)StopCoroutine(motion);motion=StartCoroutine(Tween(target,duration,bounce));}
        IEnumerator Tween(float target,float duration,bool bounce)
        {var start=transform.localScale;for(float t=0;t<duration;t+=Time.unscaledDeltaTime){float p=t/duration;float ease=bounce?1+2.7f*Mathf.Pow(p-1,3)+1.7f*Mathf.Pow(p-1,2):1-Mathf.Pow(1-p,3);transform.localScale=Vector3.LerpUnclamped(start,original*target,ease);yield return null;}transform.localScale=original*target;motion=null;}
    }
    public sealed class GentleFloat : MonoBehaviour
    {
        public float Amplitude=7,Speed=1,Phase;
        Vector2 original;
        void Start(){original=((RectTransform)transform).anchoredPosition;}
        void Update(){((RectTransform)transform).anchoredPosition=original+new Vector2(0,Mathf.Sin(Time.unscaledTime*Speed+Phase)*Amplitude);}
    }
    public sealed class FeedbackAudio : MonoBehaviour
    {
        AudioSource[] effects;AudioSource music;int voice;
        public float MusicVolume {get;private set;}
        public float EffectsVolume {get;private set;}
        public bool Muted=>MusicVolume<=0 && EffectsVolume<=0;
        readonly System.Collections.Generic.Dictionary<string,AudioClip> clips=new System.Collections.Generic.Dictionary<string,AudioClip>();
        readonly System.Collections.Generic.Dictionary<string,float> gains=new System.Collections.Generic.Dictionary<string,float>();
        [System.Serializable] public class MixEntry {public string key;public float gain=1;}
        [System.Serializable] public class MixConfig {public MixEntry[] clips;}
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TripleMazePulse(int duration);
#endif
        void Awake()
        {
            bool legacyMuted=PlayerPrefs.GetInt("TripleMaze.Muted",0)==1;
            MusicVolume=Mathf.Clamp01(PlayerPrefs.GetFloat("TripleMaze.MusicVolume",legacyMuted?0:1));
            EffectsVolume=Mathf.Clamp01(PlayerPrefs.GetFloat("TripleMaze.EffectsVolume",legacyMuted?0:1));
            effects=new AudioSource[8];for(int i=0;i<effects.Length;i++){effects[i]=gameObject.AddComponent<AudioSource>();effects[i].playOnAwake=false;effects[i].volume=EffectsVolume;}
            music=gameObject.AddComponent<AudioSource>();music.playOnAwake=false;music.loop=true;
            foreach(var key in new[]{"tap","pick","slide","land","match","blocked","win","lose","hint-sound","ambience","chain","voice-good","voice-great","voice-perfect","voice-excellent","fireworks"})clips[key]=Resources.Load<AudioClip>("Polish/"+key);
            var config=Resources.Load<TextAsset>("Polish/AudioMix");
            if(config)try{var mix=JsonUtility.FromJson<MixConfig>(config.text);if(mix?.clips!=null)foreach(var entry in mix.clips)if(entry!=null && entry.key!=null)gains[entry.key]=Mathf.Clamp01(entry.gain);}catch(System.Exception error){Debug.LogWarning("AudioMix 配置无效："+error.Message);}
            music.clip=clips["ambience"];ApplyVolumes();if(music.clip)music.Play();
        }
        float Gain(string key)=>gains.TryGetValue(key,out var value)?value:1;
        void ApplyVolumes(){music.volume=.075f*MusicVolume*Gain("ambience");foreach(var source in effects)source.volume=EffectsVolume;}
        public void SetMusicVolume(float value){MusicVolume=Mathf.Clamp01(value);ApplyVolumes();PlayerPrefs.SetFloat("TripleMaze.MusicVolume",MusicVolume);}
        public void SetEffectsVolume(float value){EffectsVolume=Mathf.Clamp01(value);ApplyVolumes();PlayerPrefs.SetFloat("TripleMaze.EffectsVolume",EffectsVolume);}
        public void SaveVolumes()=>PlayerPrefs.Save();
        public void Play(string key,float volume=.4f,float pitch=1)
        {if(EffectsVolume<=0 || !clips.TryGetValue(key,out var clip) || !clip || Gain(key)<=0)return;if(!music.isPlaying && music.clip)music.Play();var source=effects[voice++%effects.Length];source.pitch=pitch;source.PlayOneShot(clip,volume*Gain(key));}
        public void Haptic(bool match=false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(!Muted)TripleMazePulse(match?20:8);
#elif UNITY_ANDROID && !UNITY_EDITOR
            if(Muted)return;
            try{using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))using(var vibrator=activity.Call<AndroidJavaObject>("getSystemService","vibrator"))using(var version=new AndroidJavaClass("android.os.Build$VERSION")){if(version.GetStatic<int>("SDK_INT")>=26){using(var effect=new AndroidJavaClass("android.os.VibrationEffect"))using(var pulse=effect.CallStatic<AndroidJavaObject>("createOneShot",match?22L:9L,match?65:35))vibrator.Call("vibrate",pulse);}else vibrator.Call("vibrate",match?22L:9L);}}catch(System.Exception){}
#endif
        }
    }
}

namespace TripleMaze
{
    [UnityEngine.RequireComponent(typeof(UnityEngine.CanvasRenderer))]
    public sealed class RetryIcon : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper v)
        {
            v.Clear();float radius=rectTransform.rect.width*.32f,width=2.8f;
            for(int i=0;i<28;i++)
            {
                float a=(40+i*285f/28)*Mathf.Deg2Rad,b=(40+(i+1)*285f/28)*Mathf.Deg2Rad;
                var x=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var y=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int n=v.currentVertCount;
                v.AddVert(x*(radius-width*.5f),color,Vector2.zero);v.AddVert(x*(radius+width*.5f),color,Vector2.zero);v.AddVert(y*(radius+width*.5f),color,Vector2.zero);v.AddVert(y*(radius-width*.5f),color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
            }
            float end=325*Mathf.Deg2Rad;var p=new Vector2(Mathf.Cos(end),Mathf.Sin(end))*radius;var d=new Vector2(-Mathf.Sin(end),Mathf.Cos(end));var side=new Vector2(-d.y,d.x);int k=v.currentVertCount;
            v.AddVert(p+d*5,color,Vector2.zero);v.AddVert(p-d*3+side*5,color,Vector2.zero);v.AddVert(p-d*3-side*5,color,Vector2.zero);v.AddTriangle(k,k+1,k+2);
        }
    }
}
