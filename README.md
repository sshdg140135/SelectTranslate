# SelectTranslate（划词译）

Windows 11 全局划词翻译：在任意软件里选中一段外文，按一下鼠标中键，光标旁弹出卡片并流式显示译文。翻译服务由你自己配置（任意 OpenAI 兼容接口）。

- 全局生效：浏览器、Office、IDE、聊天工具都能用
- 免安装：单个 exe，解压双击即用，不需要另装运行环境
- 不打断：卡片不抢焦点，你可以继续在原应用里打字
- 可替换：接口、Key、模型都在设置里改，不绑定某一家服务商

## 当前状态

**文档阶段（M0）**，尚未开始编码。产品与技术方案已完成评审，下一步进入 M1 工程骨架。

## 文档

- 产品需求：[docs/product/PRD.md](docs/product/PRD.md)
- 需求与验收：[docs/specs/001-mvp/requirements.md](docs/specs/001-mvp/requirements.md)
- 技术设计：[docs/specs/001-mvp/design.md](docs/specs/001-mvp/design.md)
- 任务与进度：[docs/specs/001-mvp/tasks.md](docs/specs/001-mvp/tasks.md)
- 决策记录：[docs/adr/](docs/adr/)
- 文档总索引：[docs/README.md](docs/README.md)

给 AI 代理的入口见 [AGENTS.md](AGENTS.md)。

## 使用方式（v1.0 目标形态）

1. 解压 `SelectTranslate-v1.0.0-win-x64.zip`
2. 双击 `SelectTranslate.exe`
3. 首次向导里填入接口地址、Key、模型，点"测试连接"
4. 在任意应用选中外文，按鼠标中键；Esc 或点击卡片外部关闭

## 许可

待定（v1.0 发布前确定）。