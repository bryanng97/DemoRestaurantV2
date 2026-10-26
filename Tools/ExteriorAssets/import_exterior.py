"""Copy native FFDec sprite exports into a replaceable Unity exterior content pack.

Usage: python Tools/ExteriorAssets/import_exterior.py <FFDec sprite export directory>
No Flash code is executed and no pixels are modified. Stable gameplay IDs do not depend on art paths.
"""
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
source = Path(sys.argv[1])
dest = ROOT / 'Assets/_Game/Resources/Art/ExteriorPrototype'
dest.mkdir(parents=True, exist_ok=True)
# id, source class, kind, price, width, height, starter ownership, en, ms, zh
rows = [
    ('wall_cream','Tile01','Wall',100,800,360,1,'Cream bricks','Bata krim','奶油砖墙'),
    ('wall_red','Tile02','Wall',100,800,360,0,'Red bricks','Bata merah','红砖墙'),
    ('wall_stone','Tile03','Wall',150,800,360,0,'Stone wall','Dinding batu','石墙'),
    ('roof_red','GoodRoof','Roof',300,860,272,1,'Red tile roof','Bumbung merah','红瓦屋顶'),
    ('roof_gables','CrapRoof','Roof',700,860,300,0,'Gabled roof','Bumbung bertebar','山墙屋顶'),
    ('door_arch','Door01','Door',250,100,180,1,'Arched door','Pintu gerbang','拱门'),
    ('door_wood','Door02','Door',300,100,180,0,'Wooden door','Pintu kayu','木门'),
    ('window_arch','Window01','Window',150,100,150,2,'Arched window','Tingkap gerbang','拱形窗'),
    ('window_square','Window02','Window',200,110,140,0,'Square window','Tingkap persegi','方窗'),
    ('sign_wood','Sign01','Sign',150,350,90,1,'Wooden banner','Papan tanda kayu','木招牌'),
    ('sign_cream','Sign02','Sign',200,350,110,0,'Cream banner','Papan tanda krim','奶油招牌'),
    ('menu_sign','Decor01','WallDecor',300,65,85,0,'Wall menu','Menu dinding','墙上菜单'),
    ('plant_red','Ground01','Ground',250,65,100,2,'Flower pot','Pasu bunga','盆栽花卉'),
    ('flower_box','Ground03','Ground',300,140,60,0,'Flower box','Kotak bunga','花槽'),
    ('topiary','Ground04','Ground',350,65,140,0,'Topiary','Pokok hiasan','造型盆栽'),
]
items = []
translations = {'en': {}, 'ms': {}, 'zh': {}}
for id_, symbol, kind, price, width, height, owned, en, ms, zh in rows:
    matches = list(source.glob(f'DefineSprite_*_{symbol}/1.png'))
    if len(matches) != 1:
        raise RuntimeError(f'Expected one native export for {symbol}: {matches}')
    shutil.copyfile(matches[0], dest / f'{id_}.png')
    items.append(dict(id=id_, nameKey=f'exterior.item.{id_}', kind=kind, price=price,
                      width=width, height=height, unlockLevel=1, starterOwned=owned,
                      sprite=f'Art/ExteriorPrototype/{id_}'))
    for lang, name in zip(translations, (en, ms, zh)):
        translations[lang][f'exterior.item.{id_}'] = name
starter = []
for id_, x, y in [('wall_cream',500,620),('roof_red',500,272),('door_arch',500,620),
                  ('window_arch',280,560),('window_arch',720,560),('sign_wood',500,380),
                  ('plant_red',150,650),('plant_red',850,650)]:
    starter.append(dict(instance=len(starter)+1,itemId=id_,x=x,y=y,flipped=False))
(ROOT / 'Assets/_Game/Resources/Content/malaysia/exterior.json').write_text(
    json.dumps(dict(items=items, starter=starter), ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

strings = {
 'title': ('Restaurant street','Jalan restoran','餐厅街道'),
 'enter': ('Enter restaurant','Masuk restoran','进入餐厅'),
 'edit': ('Edit exterior','Ubah luaran','编辑店外'),
 'save': ('Save · {0} coins','Simpan · {0} syiling','保存 · {0} 金币'),
 'cancel': ('Cancel','Batal','取消'),
 'remove': ('Store selected','Simpan pilihan','收起选中物品'),
 'flip': ('Flip','Balik','翻转'),
 'undo': ('Undo','Buat asal','撤销'),
 'hint': ('Choose an item, then drag or tap to place. Tap existing items to move them.',
          'Pilih item, kemudian seret atau ketik. Ketik item sedia ada untuk mengalihnya.',
          '选择物品后拖动或点击放置；点击已有物品可以移动。'),
 'street_hint': ('Your restaurant keeps running while you are outside.',
                 'Restoran anda terus beroperasi semasa anda di luar.','在店外浏览时，餐厅会继续营业。'),
 'saved': ('Exterior saved','Luaran disimpan','店外布置已保存'),
 'invalid': ('Leave space between items and keep the entrance clear.',
             'Jarakkan item dan pastikan pintu masuk lapang.','请避免物品重叠，并留出门前通道。'),
 'no_coins': ('Not enough coins','Syiling tidak mencukupi','金币不足'),
 'save_failed': ('Could not save. Your coins and exterior have not changed.',
                 'Gagal menyimpan. Syiling dan luaran anda tidak berubah.','保存失败，金币和店外布置尚未更改。'),
 'online': ('Exterior is saved locally; online exterior sync is not available yet.',
            'Luaran disimpan setempat; penyegerakan dalam talian belum tersedia.',
            '店外布置保存于本机，暂未接入在线同步。'),
 'preview': ('Street preview','Pratonton jalan','街景示例'),
 'owned': ('Owned: {0}','Dimiliki: {0}','已拥有：{0}'),
 'cost': ('{0} coins','{0} syiling','{0} 金币'),
 'required': ('Keep one door, wall and roof.','Kekalkan satu pintu, dinding dan bumbung.','请保留大门、墙体和屋顶。'),
 'Wall': ('Walls','Dinding','墙面'), 'Roof': ('Roofs','Bumbung','屋顶'),
 'Door': ('Doors','Pintu','大门'), 'Window': ('Windows','Tingkap','窗户'),
 'Sign': ('Signs','Papan tanda','招牌'), 'WallDecor': ('Wall decor','Hiasan dinding','墙饰'),
 'Ground': ('Pavement decor','Hiasan laluan','街边装饰'),
}
for key, values in strings.items():
    for lang, value in zip(translations, values): translations[lang]['exterior.'+key] = value
for lang, additions in translations.items():
    path = ROOT / f'Assets/_Game/Resources/Localization/{lang}.json'
    text = path.read_text(encoding='utf-8-sig')
    data = json.loads(text)
    existing = {entry['key'] for entry in data['entries']}
    new = [dict(key=k,value=v) for k,v in additions.items() if k not in existing]
    # Append without reformatting the entire localization file.
    index = text.rfind(']')
    text = text[:index].rstrip() + ',\n' + ',\n'.join('    '+json.dumps(e,ensure_ascii=False) for e in new) + '\n  ' + text[index:]
    if new: path.write_text(text, encoding='utf-8')
print(f'Imported {len(items)} native sprite frames; catalog and en/ms/zh labels written.')
