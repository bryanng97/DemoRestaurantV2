import java.nio.file.*;
import java.util.*;
import java.awt.image.BufferedImage;
import javax.imageio.ImageIO;
import com.jpexs.decompiler.flash.SWF;
import com.jpexs.decompiler.flash.tags.DefineSpriteTag;
import com.jpexs.decompiler.flash.timeline.*;
import com.jpexs.decompiler.flash.exporters.commonshape.Matrix;

/** Reads nested display-list timelines. Does not execute ActionScript. Generated files stay private. */
class FurnitureTimelineExporter {
    static SWF swf;
    static String desiredState="idle";
    static boolean selectedState;
    static final Map<DefineSpriteTag,List<Frame>> frozen=new HashMap<>();
    static void restoreSelectors() {
        for(var pair:frozen.entrySet()){var list=pair.getKey().getTimeline().getFrames();list.clear();list.addAll(pair.getValue());pair.getKey().frameCount=list.size();}
        frozen.clear();
    }
    static void freezeSelectors(DefineSpriteTag sprite,int frame,Set<Integer> path) {
        if(!path.add(sprite.getCharacterId()))return;
        for(var state:sprite.getTimeline().getFrame(frame).layers.values()) {
            var child=swf.getCharacter(state.characterId);
            if(!(child instanceof DefineSpriteTag))continue;
            var clip=(DefineSpriteTag)child;
            if(!frozen.containsKey(clip) && state.instanceName!=null && state.instanceName.matches("sub\\d+") && clip.frameCount<=4 && clip.frameCount>1) {
                var frames=clip.getTimeline().getFrames();frozen.putIfAbsent(clip,new ArrayList<>(frames));
                var selected=frames.get(Math.min(frame,frames.size()-1));frames.clear();frames.add(selected);clip.frameCount=1;
            }
            boolean functional = clip.getTimeline().getFrameWithLabel("off")>=0 && clip.getTimeline().getFrameWithLabel("on")>=0;
            if(!frozen.containsKey(clip) && (functional || state.instanceName!=null && state.instanceName.startsWith("mc_content") && clip.getTimeline().getFrameWithLabel("idle")>=0)) {
                var frames=clip.getTimeline().getFrames();frozen.putIfAbsent(clip,new ArrayList<>(frames));
                int start=clip.getTimeline().getFrameWithLabel(functional && desiredState.equals("idle") ? "off" : desiredState);if(start>=0)selectedState=true;else start=0;
                int end=start+1;
                while(end<frames.size() && frames.get(end).labels.isEmpty())end++;
                var selected=new ArrayList<>(frames.subList(start,end));frames.clear();frames.addAll(selected);clip.frameCount=selected.size();
            }
            if(!frozen.containsKey(clip) && state.instanceName!=null && state.instanceName.matches("mc_(food|plate|icon|dish)")) {
                var frames=clip.getTimeline().getFrames();frozen.putIfAbsent(clip,new ArrayList<>(frames));
                var selected=frames.get(0);frames.clear();frames.add(selected);clip.frameCount=1;
            }
            for(int i=0;i<clip.frameCount;i++)freezeSelectors(clip,i,new HashSet<>(path));
        }
    }
    static void cycles(DefineSpriteTag sprite, int frame, Set<Integer> path, Set<Integer> counts) {
        if (!path.add(sprite.getCharacterId())) return;
        var display = sprite.getTimeline().getFrame(frame);
        if (display != null) for (var state : display.layers.values()) {
            if (!state.isVisible || state.characterId < 0) continue;
            var child = swf.getCharacter(state.characterId);
            if (child instanceof DefineSpriteTag) {
                var clip = (DefineSpriteTag)child;
                if (clip.frameCount > 1) counts.add(clip.frameCount);
                for (int i = 0; i < clip.frameCount; i++) cycles(clip, i, new HashSet<>(path), counts);
            }
        }
    }
    static int gcd(int a, int b) { return b == 0 ? a : gcd(b, a % b); }
    static void describe(DefineSpriteTag sprite,int frame,String indent,int depth) {
        if(depth>4)return;
        for(var state:sprite.getTimeline().getFrame(frame).layers.values()) {
            var child=swf.getCharacter(state.characterId);
            if(child instanceof DefineSpriteTag){var clip=(DefineSpriteTag)child;
                System.out.println(indent+state.instanceName+" id="+state.characterId+" frames="+clip.frameCount+" labels="+clip.getTimeline().getFrames().stream().filter(f->!f.labels.isEmpty()).map(f->f.frame+":"+f.labels).toList());
                describe(clip,0,indent+"  ",depth+1);
            }
        }
    }
    static String escape(String value) { return value.replace("\\", "\\\\").replace("\"", "\\\""); }
    static int[] bounds(BufferedImage image) {
        int l=image.getWidth(), t=image.getHeight(), r=-1, b=-1;
        for(int y=0;y<image.getHeight();y++) for(int x=0;x<image.getWidth();x++)
            if((image.getRGB(x,y)>>>24)>8) { l=Math.min(l,x);t=Math.min(t,y);r=Math.max(r,x);b=Math.max(b,y); }
        return r<0 ? new int[]{0,0,1,1} : new int[]{l,t,r+1,b+1};
    }
    public static void main(String[] args) throws Exception {
        try(var input=Files.newInputStream(Path.of(args[0]))) { swf=new SWF(input,true); }
        Path out=Path.of(args[2]);Files.createDirectories(out);
        boolean scan=args.length>3 && args[3].equals("scan");
        if(args.length>4)desiredState=args[4];
        StringBuilder json=new StringBuilder("{\"sprites\":["); boolean comma=false;
        for(String row:Files.readAllLines(Path.of(args[1]))) {
            if(row.isBlank())continue;
            String[] values=row.split("\t");String key=values[0], symbol=values[1];int frame=Integer.parseInt(values[2])-1;
            var character=swf.getCharacterByClass(symbol);
            if(character==null)character=swf.getCharacterByExportName(symbol);
            if(!(character instanceof DefineSpriteTag))continue;
            var sprite=(DefineSpriteTag)character;
            // Registration always comes from the original idle sprite, even when a pan swings beyond its canvas.
            restoreSelectors();String requested=desiredState;desiredState="idle";freezeSelectors(sprite,frame,new HashSet<>());
            var rect=sprite.getRect();swf.clearImageCache();
            int[] registration=bounds(SWF.frameToImageGet(sprite.getTimeline(),frame,0,null,0,rect,new Matrix(),null,null,2,true,1).getBufferedImage());
            restoreSelectors();desiredState=requested;selectedState=false;freezeSelectors(sprite,frame,new HashSet<>());swf.clearImageCache();
            int rootStart=frame,rootSpan=1;
            int label=sprite.getTimeline().getFrameWithLabel(desiredState);
            if(!desiredState.equals("idle") && label>=0){selectedState=true;rootStart=label;int end=label+1;while(end<sprite.frameCount && sprite.getTimeline().getFrame(end).labels.isEmpty())end++;rootSpan=end-label;}
            if(!desiredState.equals("idle") && !selectedState)continue;
            if(args.length>3 && args[3].equals("tree")){System.out.println(key);describe(sprite,frame,"  ",0);continue;}
            Set<Integer> counts=new TreeSet<>();
            for(int i=0;i<rootSpan;i++)cycles(sprite,rootStart+i,new HashSet<>(),counts);
            if(rootSpan>1)counts.add(rootSpan);
            if(counts.isEmpty() && desiredState.equals("idle"))continue;
            long period=1;for(int n:counts)period=period/gcd((int)period,n)*n;
            System.out.println(key+"\t"+counts+"\t"+period);
            if(scan)continue;
            // Keep every source frame. Refuse pathological composite periods rather than silently truncate loops.
            if(period>7200)throw new IllegalArgumentException("Composite loop too long: "+key+" "+period);
            List<BufferedImage> frames=new ArrayList<>();int[] first=null, box=null;
            for(int time=0;time<period;time++) {
                int phase=time%rootSpan;
                BufferedImage image=SWF.frameToImageGet(sprite.getTimeline(),rootStart+phase,time-phase,null,0,rect,new Matrix(),null,null,2,true,1).getBufferedImage();
                int[] b=bounds(image);if(first==null)first=b.clone();
                if(box==null)box=b.clone();else{box[0]=Math.min(box[0],b[0]);box[1]=Math.min(box[1],b[1]);box[2]=Math.max(box[2],b[2]);box[3]=Math.max(box[3],b[3]);}
                frames.add(image);
            }
            int w=box[2]-box[0],h=box[3]-box[1];
            List<int[]> uniquePixels=new ArrayList<>();List<BufferedImage> unique=new ArrayList<>();List<Integer> indices=new ArrayList<>();
            for(var image:frames) {
                var cropped=image.getSubimage(box[0],box[1],w,h);int[] pixels=cropped.getRGB(0,0,w,h,null,0,w);int index=-1;
                for(int i=0;i<uniquePixels.size();i++)if(Arrays.equals(pixels,uniquePixels.get(i))){index=i;break;}
                if(index<0){index=unique.size();unique.add(cropped);uniquePixels.add(pixels);}indices.add(index);
            }
            if(unique.size()<2 && desiredState.equals("idle"))continue;
            String animationKey=key+(desiredState.equals("idle")?"":"_"+desiredState)+"_loop";
            for(int i=0;i<unique.size();i++)ImageIO.write(unique.get(i),"png",out.resolve(animationKey+"_"+String.format("%03d",i)+".png").toFile());
            if(comma)json.append(',');comma=true;
            json.append("{\"key\":\"").append(escape(animationKey)).append("\",\"animationOwner\":\"").append(escape(key)).append("\",\"animationFps\":").append(swf.frameRate)
                .append(",\"animationFrames\":").append(unique.size()).append(",\"frameMap\":").append(indices)
                .append(",\"anchor\":{\"x\":").append(((registration[0]+registration[2])*.5-box[0])/w)
                .append(",\"y\":").append((box[3]-registration[3])/(double)h).append("}}");
        }
        Files.writeString(out.resolve("furniture-loops.json"),json.append("]}").toString());
    }
}
