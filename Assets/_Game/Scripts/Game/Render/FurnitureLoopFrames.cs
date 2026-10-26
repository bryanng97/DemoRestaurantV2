using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>A native nested timeline baked to packed sheets, with unchanged registration and source timing.</summary>
    public sealed class FurnitureLoopFrames : IDisposable
    {
        private readonly Sprite[] _frames;
        private readonly int[] _map;
        private readonly float _fps;
        public Vector2 AnchorOffset { get; }
        public int FrameCount => _frames.Length;

        private FurnitureLoopFrames(Sprite[] frames, SkinSprite hint)
        {
            _frames=frames; _map=hint.frameMap; _fps=hint.animationFps;
            AnchorOffset=new Vector2((hint.anchor.x-.5f)*hint.animationWidth, hint.anchor.y*hint.animationHeight)/100f;
        }

        public Sprite At(float seconds)
        {
            int frame=(int)Mathf.Repeat(Mathf.Floor(seconds*_fps), _map.Length);
            return _frames[_map[frame]];
        }

        public static FurnitureLoopFrames Load(ArtLibrary art, string key, string animationKey = null)
        {
            animationKey = animationKey ?? key;
            var manifest=art.SkinOf(key);
            if(manifest==null || !manifest.ByKey.TryGetValue(animationKey+"_loop",out var hint) ||
                hint.animationFrames<1 || hint.frameMap==null || hint.frameMap.Length<1 || hint.animationFps<=0 ||
                hint.animationColumns<1 || hint.animationRows<1) return null;
            var frames=new List<Sprite>();
            try
            {
                int capacity=hint.animationColumns*hint.animationRows;
                Texture2D sheet=null;
                for(int i=0;i<hint.animationFrames;i++)
                {
                    if(i%capacity==0) sheet=art.AnimationTexture(key,$"{animationKey}_loop_sheet_{i/capacity:D2}");
                    if(sheet==null) throw new InvalidOperationException("Missing furniture animation sheet: "+key);
                    int index=i%capacity, col=index%hint.animationColumns, row=index/hint.animationColumns;
                    var rect=new Rect(col*(hint.animationWidth+4)+2, sheet.height-row*(hint.animationHeight+4)-2-hint.animationHeight,
                        hint.animationWidth,hint.animationHeight);
                    var sprite=Sprite.Create(sheet,rect,new Vector2(.5f,0),100,0,SpriteMeshType.FullRect);
                    sprite.name=$"{animationKey}_loop_{i:D3}";frames.Add(sprite);
                }
                foreach(int index in hint.frameMap) if(index<0 || index>=frames.Count)throw new InvalidOperationException("Invalid furniture frame map: "+key);
                return new FurnitureLoopFrames(frames.ToArray(),hint);
            }
            catch(Exception error)
            {
                foreach(var sprite in frames) UnityEngine.Object.Destroy(sprite);
                Debug.LogWarning(error.Message);return null;
            }
        }

        public void Dispose() { foreach(var sprite in _frames) UnityEngine.Object.Destroy(sprite); }
    }
}
