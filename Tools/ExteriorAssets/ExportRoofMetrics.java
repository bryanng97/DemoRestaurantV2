import java.nio.file.*;
import com.jpexs.decompiler.flash.SWF;
import com.jpexs.decompiler.flash.tags.DefineSpriteTag;
import com.jpexs.decompiler.flash.types.RECT;

/** Reads native registration coordinates without executing SWF scripts. Output is generated/private. */
class ExportRoofMetrics {
    public static void main(String[] args) throws Exception {
        try (var input = Files.newInputStream(Path.of(args[0]))) {
            SWF swf = new SWF(input, true);
            StringBuilder json = new StringBuilder("{");
            for (int i = 1; i < args.length; i++) {
                var sprite = (DefineSpriteTag) swf.getCharacter(Integer.parseInt(args[i]));
                RECT rect = sprite.getRect();
                if (i > 1) json.append(',');
                json.append('"').append(args[i]).append("\":{\"xMin\":").append(rect.Xmin)
                    .append(",\"yMin\":").append(rect.Ymin).append('}');
            }
            System.out.println(json.append('}'));
        }
    }
}
