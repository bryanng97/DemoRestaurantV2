# 玩法对照：参考游戏 vs 我们现在（室内 + 室外）

2026-10-24 整理。参考资料来源见 `Docs/ExteriorPrototype.md` 的 Provenance（公开的反编译客户端与复刻服务器）。
本文件只用我们自己的话描述规则，不含原版代码、数据表或文字。

2026-10-25 更新：已加入玩家清盘任务。点击空盘或脏桌子，任务按点击顺序逐个执行，
每个 5 秒（`rules.json` 的 `playerCleanSeconds`）；等待中的任务可再次点击取消。
玩家接手时释放员工的清理任务；金币仍在现有吃完结账流程发放，清盘不重复发奖。
可点击的空盘有淡光提示，任务有进度条，完成有程序绘制的闪光。
规则核对来源：[ClearEmptyPlateTask.as](https://github.com/Dippys/Restaurant-City-client/blob/main/scripts/com/playfish/games/cooking/usertask/ClearEmptyPlateTask.as)，
核对了队列、取消等待任务、5 秒时长和发光监听；本地实现独立编写，没有复制源码或新增参考美术。

**原则建议：规则照参考游戏，数值照我们的经济表格（`Docs/Design/Global_FnB_City_Economy_*.xlsx`）。**
下表中「建议」一栏标记：✅ 已一致　🔧 照参考改　🆕 新做　⏸ 保留我们的做法　❓ 需要你决定

---

## A. 室内

### A1. 客人来店
| | 内容 |
|---|---|
| 参考 | 一个「人气（demand）」数值决定来客频率（默认约 10 秒一位）。客人**开心离开 +1、不开心 −1**，人气直接影响来客速度。没员工营业时人气每小时 −10。约 1/20 的客人是好友的头像。 |
| 我们 | 来客间隔 = 由星级（0–5）和等级计算（表格公式）。服务快慢只影响星级。 |
| 建议 | ⏸ 保留星级 + 等级公式（已是表格设计），但 🔧 **加入「开心/不开心」回馈**：开心 +星级、不开心 −星级（我们已有类似机制，只需把「垃圾太多、没位子、没厕所」也算成不开心）。 |

### A2. 入座与等待
| | 内容 |
|---|---|
| 参考 | 随机挑空椅子；没位子在门口等约 5 秒就走；**有椅子没桌子**、**桌子还没清**会先抱怨再等；点餐等服务员约 10 秒；等上菜最多 120 秒。 |
| 我们 | 有排队区（满了才拒绝）；点餐后有等菜耐心；脏桌子不能坐。 |
| 建议 | ✅ 大致一致。🔧 可加「没桌子的椅子不能坐」的规则和表情。 |

### A3. 点菜与菜单
| | 内容 |
|---|---|
| 参考 | 先随机选一个类别（前菜／主菜／甜点，15 级后加饮料），再从该类已上菜单的菜随机挑一道。每类 1→3 格，由等级决定。 |
| 我们 | 菜单按类别给格子（表格的 Levels）；客人从「上菜单的全部菜」随机挑。 |
| 建议 | 🔧 改成**先选类别再选菜**（让每类都会被点到）。 |

### A4. 烹饪、送餐、清桌
| | 内容 |
|---|---|
| 参考 | 厨师煮菜时间随体力变化（体力高最快，低于 20% 最慢）；家具可带「操作加速 %」。服务员：点餐、上菜、收空盘、做饮料（要饮料机）。吃饭 25 秒后留下空盘，**桌子要清了才能再坐**。 |
| 我们 | 厨师在炉子按 `cookSeconds`；体力分档降速；服务员送餐；脏桌子由清洁工/空闲服务员清。 |
| 建议 | ✅ 已一致。🆕 **饮料机**（15 级饮料开放时再做）。🆕 家具「加速 %」属性（例如高级炉子）——对应表格的 wok_burner「较快的炉子」。 |

### A5. 付款、分数
| | 内容 |
|---|---|
| 参考 | 付菜价金币 + 美食分数 `1 + 0.2 × (菜品等级 − 1)`。**没有小费**，客人**不掉食材**。 |
| 我们 | 菜价 + XP（表格公式）；表格有小费（氛围值）与客人掉食材 25%（尚未做）。 |
| 建议 | ❓ **小费、客人掉食材要不要做？** 参考游戏没有；你的表格有。 |

### A6. 垃圾
| | 内容 |
|---|---|
| 参考 | **不是客人丢的**：全店计时器约每小时生成 1 个垃圾，最多 15 个。玩家点垃圾可得少量金币 + 1 分；清洁工也会捡。**垃圾越多，客人到门口越可能直接离开**（15 个约 75%）。 |
| 我们 | 没有垃圾（只有脏桌子）。 |
| 建议 | 🆕 照参考做。这是「每小时回来看一下」的留存钩子，清洁工可自动处理。 |

### A7. 厕所、洗手台
| | 内容 |
|---|---|
| 参考 | 8 级起约 1/3 付完钱的客人去厕所（10 秒）再洗手（4 秒）。没厕所会不开心。厕所和娱乐机器有使用次数，**用久会坏**，要清洁工或玩家修。 |
| 我们 | 没有。表格有 toilet（8 级，Phase 1 = No）。 |
| 建议 | 🆕 照参考做，但放在 Phase 1 之后（10 级上限内 8 级会碰到，❓ 要不要放进 Phase 1）。 |

### A8. 员工与体力
| | 内容 |
|---|---|
| 参考 | 员工是好友头像或随机头像；职位：厨师/服务员/清洁工/休息。人数上限按等级 2→9。体力 = **4 小时工作时间池**，工作时 1:1 消耗，**休息 3 倍速回复**（床类家具再加速），**喂食物**立即补一段时间。没体力会显示「需要食物」并停工。解雇有扣分。 |
| 我们 | 体力 0–100 分档降速，休息回复，用道具（kopi）补体力；员工位按等级。 |
| 建议 | 🔧 体力改成「工作时间池」的说法（数值照表格：约 3 小时、零食补 25%、正餐补满、免费休息 60 分钟）。❓ **补体力用「道具」还是「喂餐厅的菜」？** |

### A9. 菜谱学习与菜品等级
| | 内容 |
|---|---|
| 参考 | 每道菜是**固定的食材清单**（照清单学，不用试组合）。每次再用一整套食材把菜升一级，最高 10 级；**菜的图会随等级变**；每升一级给一次性奖励分数（25 → 800 递增）。 |
| 我们 | 另一个 AI 已加入「学习菜谱 / 升级菜谱」（照清单），保留「试食材组合」的实验。 |
| 建议 | ✅ 已一致。❓ **「试食材组合发现菜谱」要保留吗？**（参考游戏没有） |

### A10. 等级与分数
| | 内容 |
|---|---|
| 参考 | 美食分数决定等级（约 68 级）。每级决定：房间大小、员工上限、每类菜数、农地块数、可存布局数、升级金币。其他得分：送礼、雇人、卖家具、买东西、交易。离线收益上限 7000 金币。 |
| 我们 | XP 曲线（表格）；等级决定菜单格、员工位、农地；升级给金币；离线收益上限 4 小时、25%。 |
| 建议 | ✅ 结构一致，数值照表格。🔧 可加「送礼、雇人、卖家具」也给 XP。 |

### A11. 每日、特殊访客
| | 内容 |
|---|---|
| 参考 | 每日登入送食材（看连续天数），走邮件；每日问答走邮件。特殊访客是宣传角色，可点；主题客人造型（圣诞、僵尸……）。 |
| 我们 | 表格有登录连签、问答、惊喜访客（旅行厨师）——尚未做。 |
| 建议 | 🆕 照表格做；奖励统一走**邮箱**（之后服务器发放）。 |

---

## B. 室外

### B1. 街景（另一个 AI 已做原型）
| | 内容 |
|---|---|
| 参考 | 横向街道，店面一字排开（约 420 px 间距）。三种街：**好友街**（自己第一，按分数排）、**随机街**、**美食街排行**（前 20/50，头上显示名次）。每第 5 格放「邀请好友」或广告看板。点店面 → 镜头拉近 → 进入那间店；点自己的直接回家。季节性街景（万圣节）。 |
| 我们 | 有正面街景、自己的店面可编辑；两旁是静态示例。 |
| 建议 | 🔧 加「好友街 / 随机街 / 排行街」切换；Phase 1 先放 1 间假好友。 |

### B2. 店面外观
| | 内容 |
|---|---|
| 参考 | 一栋一个的槽：主体、屋顶、墙面花纹、招牌（**可自己打字**）；其他门窗、遮阳棚、装饰可在一个范围内拖动、翻转。新玩家随机给一套。外观与室内完全分开。 |
| 我们 | 另一个 AI 已做：屋顶、墙、门、窗、招牌、墙上装饰、门前装饰，可移动/翻转/收纳。 |
| 建议 | ✅ 大致一致。❓ **招牌要不要能自己打字？**（需要不当文字过滤） |

### B3. 街道特效装饰
参考：雪花、落叶、爱心、泡泡喷泉等，**纯装饰、一直播放**，随外观物品附带。建议 🆕 之后加，美术到位再做。

### B4. 拜访好友
| | 内容 |
|---|---|
| 参考 | **第一次拜访某好友**送 1 个随机食材。每天每位好友可做 1 次「帮忙」：随机出现一个小问题（着火、漏水、蘑菇、臭鼬…）点掉；奖励**递减**：当天第 1 次 500、第 2 次 100、第 3–5 次 50、之后 15（我们的表格是 100/25/10/3）。也可帮好友捡垃圾、浇农地。 |
| 我们 | 没有。表格有拜访奖励递减。 |
| 建议 | 🆕 照参考做「帮忙」小事件 + 首访送食材；数值照表格。 |

### B5. 农地
| | 参考 | 我们现在 |
|---|---|---|
| 开放 | 6 级才开第 1 块，最多 9 块 | 表格：3/6/9 级 |
| 种子 | 一律同价，**作物由服务器随机**，等级越高越可能出稀有 | 300 金币，按稀有度随机 |
| 浇水 | 每次 **+3 小时**，最多存 **9 小时**；只有湿的时候才长 | 每次「补满」9 小时 |
| 成熟 | 湿润累计 **48 小时** | 每种作物 24–36 小时（表格） |
| 收成 | **1 个**，可能不能交易 | 3 个（rules.json） |
| 社交 | **可帮好友浇水**（算拜访帮忙，有奖励） | 没有 |

建议：🔧 浇水改成「+3 小时、上限 9 小时」，🆕 帮好友浇水；❓ **收成数量、开放等级照参考还是照表格？**

---

## C. 后端（补进 `Docs/backend-api-requests.md`）
- 价格一律服务器计算；有任何购买/卖出时忽略客户端金币总数，能算价的先扣，异常的隔离并记录。
- 存档带 session token + **严格递增的存档版本**（compare-and-swap），防重放、防并发；每个玩家的存档排队处理；存档前先快照。
- 一次性奖励（首访、问答、每日）用唯一的「(玩家, 种类, key)」标记防重复——跟我们的 Idempotency-Key 同理。
- 计时（作物、员工体力）用服务器时钟；随机结果（作物稀有度）由服务器决定。
- 异常检测只记录、不自动处罚：时钟快转/倒退、等级与分数不符、超出解锁、负数余额、未知物品。
- 参考服务器的漏洞要避免：分数、垃圾、人气、收成是否成熟、客人掉落都信任客户端。

---

## D. 需要你决定（汇总）
1. **小费、客人掉食材**：照参考（不做）还是照表格（做）？
2. **垃圾**：照参考做（计时生成、影响来客、清洁工处理）？
3. **厕所/洗手台、用久会坏**：要不要放进 Phase 1？
4. **补体力**：道具，还是喂餐厅的菜？
5. **「试食材组合发现菜谱」**：保留还是拿掉？
6. **客人点菜**：改成先选类别再选菜？
7. **农地**：浇水 +3 小时/上限 9；收成数量与开放等级照参考还是表格？
8. **招牌自己打字**：要不要？
9. **拜访好友的「帮忙小事件」+ 首访送食材**：要不要做？

---

## E. 决定（2026-10-24）

**规则照参考游戏，数值先用参考的值放进 JSON，之后再按经济表格调整。**

| # | 项目 | 决定 |
|---|---|---|
| 1 | 小费、客人掉食材 | 不做（参考没有） |
| 2 | 垃圾 | 做：计时生成、上限、点击清掉得金币与分数、清洁工捡、垃圾多客人会走 |
| 3 | 厕所/洗手台、用久会坏 | 做：8 级起部分客人去厕所；没有就不开心；使用次数到了会坏，清洁工或玩家修 |
| 4 | 补体力 | 工作时间池（约 4 小时）、休息 3 倍速回复、喂食物道具立即补 |
| 5 | 试食材组合 | 拿掉，只照清单学习/升级 |
| 6 | 客人点菜 | 先随机选类别，再选该类别的菜 |
| 7 | 农地 | 浇水 +3 小时、最多存 9 小时、湿润累计 48 小时成熟、收成 1 个、6 级开放、作物按等级随机稀有度；可帮好友浇水 |
| 8 | 招牌打字 | 做（需要文字过滤） |
| 9 | 拜访好友 | 首访送食材 + 每天每位好友一次「帮忙小事件」，奖励递减 |
| 10 | 来客 | 改用「人气」：开心离开 +1、不开心 −1，人气决定来客速度；无人营业时人气下降。星级改为将来的好友评分 |

### 开发顺序
1. **核心来客**：人气取代星级驱动来客；开心/不开心回馈；先选类别再选菜；拿掉试组合；没桌子的椅子不能坐。
2. **垃圾**。
3. **厕所/洗手台与损坏维修**。
4. **员工体力改成工作时间池**。
5. **农地规则更新**。
6. **拜访好友**：假好友、帮忙小事件、首访礼物、帮浇水；街景好友街/随机街/排行街。
7. **招牌打字**。


2026-10-25 furniture animation update: Tools/RefProto/furniture_export.py bakes nested native loops without executing ActionScript. Static directions and labelled idle states stay fixed; native timing, full composite periods and registration are retained. Identical frames are deduplicated and packed into 2048px sheets under the ignored private skin. FurnitureLoopFrames plays the loops on placed furniture, wall decor and held previews. The current pack provides 57 animated views across 139 sheets; static source furniture stays static. NPC hand washing reuses the source cooking hand motion and faces the sink. Only explicitly marked outdoor decor can stand on the designated street cells; local legacy indoor street placements move to storage without losing ownership or money.


2026-10-25 service visual regression update: kitchen artwork now follows the reference idle/cooking/done states. Cooking reads the chef's active stove; the ticket's stove is assigned only once ready. Native ready cookware replaces the fallback food drawn on top, until waiter pickup. urniture_export.py --kitchen-states bakes both facing sets and preserves idle registration; nimationOwner ties each state to its base skin. Bathroom activities show localized status text, fixture icons and a blue completion bar. BathroomProgress uses actual configured durations, and washing uses the cooking hand motion facing the sink.

The nine previous failures were: indoor touch (1), exterior touch (1), extensible catalog count (1), native shelf dimensions in two orientations (2), explicit legacy shelf migration for save versions 1 and 2 (2), and table occlusion in two orientations (2). The tests now use their intended art/geometry and real UI coordinate space; touch fixtures account for the new-purchase finger offset and pending selection. Migration also preserves stored ownership and wall settings. All 20 concept-grounding cases now explicitly select the base art instead of skipping when a private skin is installed. Full validation: 420 passed, 0 failed, 0 skipped, in Logs/service-full-tests.xml.

Reference: https://github.com/Dippys/Restaurant-City-client/blob/main/scripts/com/playfish/games/cooking/actors/ChefActor.as and actors/Customer.as.
