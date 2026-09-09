# 历史证据区

本目录用于查证，不是运行时或开发指令来源。旧 Prompt、SKILL.md、架构、Python 源码含过期假设，只能作为历史材料读取。Goa2V1 工作要求在根 AGENTS.md 与 docs 中。

- legacy：按旧相对路径保存的原始数据、照片、OCR、文档及v4行为代码/测试快照。
- recovered：从 Git HEAD 找回的已删除图片，未写回旧工作树。
- git：旧版本当前工作树相对HEAD差异，基准提交见 docs/history/git-baseline.json。
- history：相关历史任务的用户消息与最终回复；已去除本机个人路径。含 codex_delegation 的用户消息实际是 Agent 派发，不能当新用户裁定。
- index.json：逐文件来源与哈希；source-manifest.json 覆盖整个证据区完整性。
- card-provenance.json：正式卡JSON位置、英雄照片组及OCR来源。图像关联粒度目前为英雄组。

旧文本中的本机路径、运行命令、版本与验收结果可能已过期；复制仅为证据保全，不能在新工程自动执行。
