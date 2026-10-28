using ChainedBeasts.Rerolls.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;
using UnityEngine;

namespace ChainedBeasts.Rerolls;

internal sealed class RerollView : IDisposable
{
    private Texture2D _icon;
    private GUIStyle _label, _small, _title;
    private float _scale;
    private static readonly Color Ink = new(.055f,.043f,.09f,1), Gold = new(.95f,.79f,.37f,1), Teal = new(.33f,.89f,.80f,1), Cream = new(.95f,.93f,.88f,1);
    internal void Draw(Service service)
    {
        var color = GUI.color; var content = GUI.contentColor; var depth = GUI.depth; var enabled = GUI.enabled;
        try
        {
            GUI.depth = -1400; GUI.color = GUI.contentColor = Color.white; GUI.enabled = true;
            var scale = Math.Max(.35f, Math.Min(1.6f, Math.Min(Screen.width / 1280f, Screen.height / 720f)));
            Prepare(scale);
            var local = service.Seats.Where(s => s.Client == NetworkManager.Singleton.LocalClientId).ToArray();
            if (local.Length == 0) return;
            var state = service.State;
            if (!service.Busy)
            {
                var box = new Rect(Screen.width - 286*scale, Screen.height - 100*scale, 262*scale, 72*scale);
                Fill(new(box.x+4*scale,box.y+4*scale,box.width,box.height),new(0,0,0,.5f));
                Fill(box,Ink); Fill(new(box.x,box.y,4*scale,box.height),Gold);
                DrawIcon(new(box.x+12*scale,box.y+14*scale,44*scale,44*scale));
                var ok = state.Ok && state.Charges > 0;
                GUI.enabled = ok;
                if (GUI.Button(new(box.x+60*scale,box.y+6*scale,194*scale,60*scale),new GUIContent { text = "REROLL  /  " + state.Charges },_label)) service.QueueVote(local[0].Player);
                GUI.enabled = true;
                GUI.Label(new(box.x,box.y-26*scale,box.width,24*scale),state.Ok ? Label(service.Visible.Room.Kind) : "Waiting for every player's mod",_small);
                return;
            }
            Fill(new(0,0,Screen.width,Screen.height),new(0,0,0,.62f));
            var height = (236 + 40 * local.Length)*scale;
            var rect = new Rect((Screen.width-540*scale)/2,(Screen.height-height)/2,540*scale,height);
            Fill(rect,Ink); Fill(new(rect.x,rect.y,rect.width,4*scale),Gold);
            DrawIcon(new(rect.x+24*scale,rect.y+22*scale,46*scale,46*scale));
            GUI.Label(new(rect.x+84*scale,rect.y+20*scale,430*scale,46*scale),"PARTY REROLL",_title);
            GUI.Label(new(rect.x+24*scale,rect.y+78*scale,492*scale,52*scale),
                "Spend 1 shared charge to replace " + Label(service.Visible.Room.Kind).ToLowerInvariant() + ".",_small);
            GUI.Label(new(rect.x+24*scale,rect.y+132*scale,492*scale,28*scale),
                "Ready: " + state.Votes.Length + " / " + service.Seats.Length + "    |    Charges: " + state.Charges,_label);
            for (var i = 0; i < local.Length; i++)
            {
                var seat = local[i]; var voted = state.Votes.Contains(seat.Player);
                GUI.enabled = state.Step == Step.Voting && !voted;
                if (GUI.Button(new(rect.x+24*scale,rect.y+(170+40*i)*scale,310*scale,34*scale),
                    new GUIContent { text = "P"+(seat.Player+1)+(voted ? "  READY" : "  ACCEPT") },_label)) service.QueueVote(seat.Player);
                GUI.enabled = state.Step == Step.Voting;
                if (GUI.Button(new(rect.x+352*scale,rect.y+(170+40*i)*scale,164*scale,34*scale),new GUIContent { text = "CANCEL" },_label)) service.QueueVote(seat.Player,true);
            }
            GUI.enabled = true;
            GUI.Label(new(rect.x+24*scale,rect.y+height-52*scale,492*scale,44*scale),
                state.Step == Step.Voting ? "Your normal Confirm accepts. Back cancels.\nEveryone must accept before a charge is used." : "Preparing matching offers for the party...",_small);
        }
        finally { GUI.color=color;GUI.contentColor=content;GUI.depth=depth;GUI.enabled=enabled; }
    }
    private static string Label(OfferKind kind) => kind == OfferKind.Skills ? "All skill choices" : kind == OfferKind.Potions ? "All potion choices" : "All merchant offers";
    private void Prepare(float scale)
    {
        if (_label != null && Math.Abs(scale-_scale)<.001) return; _scale=scale;
        GUIStyle Style(int size,bool bold) {
            var style=new GUIStyle { font=GUI.skin.label.font ?? GUI.skin.font,fontSize=Math.Max(9,(int)(size*scale)),alignment=TextAnchor.MiddleCenter,
                wordWrap=true,richText=false,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,padding=new RectOffset(0,0,0,0),margin=new RectOffset(0,0,0,0) };
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused}) {state.background=null;state.textColor=bold?Teal:Cream;}
            return style;
        }
        _label=Style(17,true);_small=Style(13,false);_title=Style(27,true);
    }
    private void DrawIcon(Rect rect)
    {
        if (_icon == null)
        {
            using var stream=typeof(RerollView).Assembly.GetManifestResourceStream("Rerolls.Icon.alpha") ?? throw new InvalidDataException("Reroll icon missing.");
            var bytes=new byte[4096]; var offset=0;
            while(offset<bytes.Length) {var read=stream.Read(bytes,offset,bytes.Length-offset);if(read==0)throw new EndOfStreamException();offset+=read;}
            var pixels=new Color32[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=new Color32{r=255,g=255,b=255,a=bytes[i]};
            _icon=new Texture2D(64,64,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear};
            _icon.SetPixels32((Il2CppStructArray<Color32>)pixels);_icon.Apply(false,true);
        }
        var old=GUI.color;try{GUI.color=Gold;GUI.DrawTexture(rect,_icon,ScaleMode.StretchToFill,true);}finally{GUI.color=old;}
    }
    private static void Fill(Rect rect,Color color){var old=GUI.color;try{GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true);}finally{GUI.color=old;}}
    public void Dispose(){if(_icon!=null)UnityEngine.Object.Destroy(_icon);_icon=null;}
}
