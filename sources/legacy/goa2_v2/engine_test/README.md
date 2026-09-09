# GoA2 卡牌测试器

这个目录是 v2 旁边的规则测试器，用来直接测试卡牌效果，不依赖浏览器和四个标签页。

运行：

```powershell
cd engine_test
python run_card_tests.py
```

结构：

- `driver.py`：测试驱动，负责创建房间、设置英雄/位置、调用和网页相同的 API 路由。
- `test_cards.py`：当前回归测试集合。
- `run_card_tests.py`：命令行入口。

后续新增测试时，优先使用 `GameDriver` 的高层动作方法：

- `set_hero(seat, hero_key)`
- `place(seat, x, y)`
- `set_reveal_with_card(seat, card_id)`
- `main_action(seat, x, y)`
- `move(seat, x, y, fast=False)`
- `choose_extra_target(seat, target_seat)`
- `discard_card(seat, card_id)`
- `forced_move(seat, x, y)`
- `defend(seat, card_id)`

这样可以把“规则是否正确”从“网页 UI 是否好用”里拆出来。
