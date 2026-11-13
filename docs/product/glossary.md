# 术语表

统一口径，文档、界面文案、代码命名都以此为准。新增术语先加到这里，再使用。

## 产品与交互

| 术语 | English | 含义 |
|---|---|---|
| 划词翻译 | select-to-translate | 选中文本后按键触发翻译的交互方式 |
| 中键按下 | middle-button press | 鼠标中键按下的瞬间，**不含**滚轮滚动、长按与拖动 |
| 取词 | selection capture | 从当前前台应用读取用户选中的纯文本 |
| 卡片 | result card | 显示原文与译文的非激活置顶浮窗 |
| 图钉 | pin | 让卡片在点击外部时不关闭的固定状态 |
| 轻提示 | toast | 1.5 秒自动消失、不抢焦点的短提示 |
| 目标语言 | target language | 译文希望使用的语言，默认简体中文 |
| 旁路修饰键 | bypass modifier | 按住后让快捷键恢复原生行为的修饰键，默认 Ctrl |
| 黑名单 | blocklist | 命中后不触发翻译的进程列表 |
| 便携模式 | portable mode | 配置与日志写在程序目录的运行方式 |

## 服务商与协议

| 术语 | English | 含义 |
|---|---|---|
| 服务商 | provider | 提供翻译能力的后端服务 |
| 服务商类型 | provider type | 用户可见的选择项，如"OpenAI 兼容""Google 翻译" |
| 服务商适配器 | provider adapter | 把某家服务商的协议映射到统一接口的实现 |
| OpenAI 兼容 | OpenAI-compatible | 请求/响应格式与 OpenAI Chat Completions 一致的接口 |
| 流式 | streaming | 服务端边生成边返回，客户端逐段显示 |
| 首字 | first token | 用户看到第一段译文的时间点 |
| 温度 | temperature | 生成随机性参数，翻译场景建议 0–0.3 |
| 系统提示词 | system prompt | 每次请求都发送的翻译指令模板 |

## 文档与流程

| 术语 | English | 含义 |
|---|---|---|
| PRD | Product Requirements Document | 产品需求文档，只回答做什么/为什么 |
| 需求条目 | requirement | `requirements.md` 中带编号、可测试的句子 |
| 验收用例 | acceptance criterion | `AC-xx`，Given/When/Then 格式 |
| 追溯矩阵 | traceability matrix | FR ↔ REQ ↔ AC 的对应关系 |
| 设计文档 | design doc | 回答怎么做，不新增用户可见行为 |
| ADR | Architecture Decision Record | 一次决策的背景、选项、结论与后果 |
| 规格驱动开发 | SDD | 先写规格、再生成代码，代码服务于规格 |
| 里程碑 | milestone | 可演示的阶段成果，M1–M4 |

## 命名对照（代码）

| 文档用语 | 代码建议命名 |
|---|---|
| 取词 | `SelectionCapture` / `ISelectionProvider` |
| 卡片 | `PopupWindow` / `ResultCard` |
| 服务商适配器 | `ITranslationProvider` |
| 服务商注册表 | `ProviderRegistry` |
| 语言检测 | `LanguageDetector` |
| 配置 | `AppConfig` / `ConfigService` |
| 轻提示 | `Toast` |

## 不使用的说法

| 避免 | 改用 | 原因 |
|---|---|---|
| 划词 | 选中文本 | "划词"在部分用户看来含"自动触发"之意 |
| 弹窗 | 卡片 | "弹窗"暗示抢焦点 |
| 插件 | 功能 | v1.0 没有插件体系 |
| 后台翻译 | 按键翻译 | 避免暗示后台采集 |