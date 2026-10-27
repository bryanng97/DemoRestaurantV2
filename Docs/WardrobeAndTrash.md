# 衣柜与地面垃圾（2026-10-27）

## 入口与操作

点击左上角头像，打开「外貌／服装」。服装页支持试穿，购买后保存搭配；「我的衣柜／全部商店」按钮切换已拥有与完整目录。免费初始服装始终保留；付费服装只能出售未穿着的款式，返还目录价格的一半。随机搭配只选择已拥有、已解锁的部件。帽子、上衣、裤子的初版价格在 `Resources/Content/malaysia/avatar.json`，可由策划调整。

当前 RC 皮肤已接通原角色模型、分层贴图与原动画。外貌／服装页可以点击试穿、随机搭配、购买并保存；不再使用旧的卡通占位角色，也不再禁用这些操作。默认未编辑的主人继续使用原烘焙帧；保存搭配后，头像、预览和店内主人共用原模型生成的外观，支持前后视角、走路、端菜、站立、烹饪、坐下、吃饭、清洁及维修。

当前小型目录映射到原版可用的发型、帽子、上衣和裤裙，界面显示对应的原版名称。兼容用的旧部件 ID 保留，已有所有权和存档可继续读取；它不是 RC 原版全部服装目录的完整导入。颜色选择通过原贴图着色实现，皮肤／眼睛／眉毛等也使用原图层。新搭配按需渲染、按外观缓存，保留预览和正在使用的外观，释放不用的生成图片。

地面垃圾可以直接点击清除；清洁工在没有维修和脏桌任务时前往垃圾处清理。点击清洁工正在处理的垃圾会立即完成该件并释放员工任务，不会重复发奖。清洁工休息、解雇和换岗会释放任务。垃圾不阻挡寻路；装修后被家具覆盖或无法从门口到达的垃圾会移除，不发奖励。

## 数值与存档

`rules.json` 的 `trash` 定义生成间隔、上限、清理耗时、拒客概率及经验：当前每 3600 秒一件、最多 15 件、清洁工基础处理 3 秒，每件垃圾增加 5% 的门口离开概率。游客清理每件获得经济表 `trashCoins` 金币和 1 经验。垃圾与客人是否用餐无关；脏桌子保持独立流程。

离线累积使用现有离线结算的封顶时长，只生成至数量上限，满额时不积累待释放的生成队列。旧存档没有垃圾时从空地面开始。存档版本 7 保存衣柜所有权、是否启用自定义外观、垃圾位置与生成计时。旧存档已穿着的服装继续拥有，不批量赠送整个付费目录。

## 线上同步（2026-10-27，规则集 malaysia v3）

后端规则集 v3 加入 `catalog.wardrobe`（全部 58 个部件：槽位为 `AvatarSlot` 的 snake_case，如 `hair_style`；价格、解锁等级）和 `rules.trash`。合同见后端仓库 `docs/mobile-api-v2.md`「Wardrobe and trash」。

- 购买、出售、保存外观和清理垃圾与其他玩法一样记入 `ServerLog`（`wardrobe_buy`／`wardrobe_sell`／`wardrobe_look`／`trash_clean`），离线和游客状态也会记录，同步时由服务器用自己的价格和时钟重新校验。`wardrobe_look` 只保留最新一条，只发送服务器认识的部件。
- 服务器状态的 `wardrobe` 是账号的拥有列表与搭配：没有待发送交易时，它替换设备上的拥有列表；账号未拥有的付费部件会被换下。因此登出后购买、或游客购买后登录已有账号，都不能保留未付款的服装。账号还没有保存过搭配时，设备的自定义外观会上传一次。
- 垃圾奖励由服务器按自己的时钟限额：每 `spawnSeconds` 一件、最多累积 `maxPieces` 件（从未领取的账号从满额开始）。超出时该条被拒（`TRASH_NOT_READY`），下一次状态修正金币。
- 已登录且服务器规则没有衣柜（v3 之前）时，`WardrobeTradingOpen` 为假：仍可试穿，不开放买卖，清理垃圾不发奖励。

## 原版素材与导入

垃圾图使用 `trash_banana_peel`、`trash_pizza_slice`、`trash_soda_can`、`trash_chicken_bone`、`trash_apple_core`，分别对应 RC 客户端 `WorldRestaurantPlay.as` 的 `DEFAULT_TRASH_ITEMS`：BananaPeel、PizzaSlice、SodaCan、ChickenLeg、AppleCore。图片来自项目原先记录的同一保存档案的 interior SWF 导出，直接复制，没有重新绘制；`Art/RefProto/trash-provenance.json` 记录源符号和 SHA-256。使用皮肤原生像素比例显示。缺图的其他皮肤才使用简易占位图。

RC 客户端仓库提供源码和符号名称，图片和模型在独立的资源档案中。沿用 `Docs/team-assets.md` 的原始档案来源与 prototype 使用政策。

重现导入：`Tools/RefProto/import_avatar_runtime.ps1 -ExportRoot <GameAssets-0.9.143a 导出目录>`。它导入原模型、232 个图层、部件与动画配置和上述五张垃圾图。`NativeAvatarImportRules` 设置 Legacy 动画和可读取的原图层；`NativeAvatarRenderer` 合成 512×512 原纹理，采样原模型动画，通过独立渲染命令输出缓存帧。原生部件映射在 `Art/RefProto/AvatarRuntime/native-avatar.json`，显示名在 `avatar.native.*` 本地化条目中。原版资源继续受 RefProto 发布检查约束。

## 验证

当前原模型换装与原图垃圾实现通过 Unity 6000.6.4f1 的全套 EditMode 检查：503 通过、0 失败、0 跳过（`Logs/native-wardrobe-final-tests.xml`）。五张垃圾图的 SHA-256 均与原始导出一致。基础版与先前只读画风修正的历史结果分别在 `Logs/wardrobe-trash-final-tests.xml` 和 `Logs/rc-avatar-regression-tests.xml`。同时修正了原有 FurnitureLoopFrames 在编辑模式释放生成帧时使用运行时销毁方式的问题。

`WardrobeAndTrashTests` 覆盖交易定价与重复购买、余额／等级校验、未拥有不可穿、随机搭配、旧存档迁移、存档往返、已穿不可卖、线上经济限制、垃圾上限／合法位置、重复点击、清洁工交接和休息／解雇。

`WardrobeAndTrashPresentationTests` 检查 RC 模型的实际按钮点击／预览／保存、旧换装存档、头像一致、原动作与前后视角、携带物位置，并比较皮肤、眼睛、眉毛、发型、衣物与颜色变化的实际图片像素。也覆盖程序绘制皮肤的旧流程、垃圾点击／装修模式屏蔽，以及五张原图全部存在且未使用简易占位图。有图形设备时输出预览到 `Logs/wardrobe-native-working.png`、`Logs/wardrobe-procedural-preview.png` 和 `Logs/trash-current-scene.png`。
