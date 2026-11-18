# AGENTS.md

给 AI 编码代理的项目入口。**只放地图、命令、红线**，细节一律链接到 `docs/`，避免本文件变成第二份会漂移的文档。

## 项目是什么

SelectTranslate（划词译）：Windows 11 桌面常驻工具。在任意应用中选中外文，按一下鼠标中键，光标旁弹出卡片并流式显示译文。翻译走用户自配的 OpenAI 兼容大模型接口。

## 当前阶段

**v0.9.0 可分发预览版已打包**：取词、语言短路、翻译（非流式）、错误映射、卡片交互（图钉/拖动/Esc）、键盘热键、托盘与提权重启、设置最小页；90 个单测通过。下一步（v1.0）：SSE 流式、剪贴板全格式还原、设置五页/黑名单/明文警告、干净机验收。

## 文档地图（按顺序读）

| 顺序 | 文件 | 回答什么问题 |
|---|---|---|
| 1 | `docs/product/PRD.md` | 做什么、给谁、为什么、什么不做 |
| 2 | `docs/specs/001-mvp/requirements.md` | 可测试的需求（REQ）与验收（AC） |
| 3 | `docs/specs/001-mvp/design.md` | 怎么做：架构、钩子、取词、卡片、配置 |
| 4 | `docs/specs/001-mvp/tasks.md` | 按什么顺序做，当前进度 |
| 5 | `docs/adr/` | 为什么这样选，否决了什么 |
| 6 | `docs/ops/release.md` | 怎么打包、怎么发给别人 |
| — | `docs/product/glossary.md` | 术语口径 |
| — | `docs/product/competitive-research.md` | Snipaste / uTools 调研结论 |

完整索引见 `docs/README.md`。

## 命令

本机已全局安装 **.NET SDK 10.0.403**（`C:\Program Files\dotnet`，含 WindowsDesktop 运行时）。

- 构建：`dotnet build SelectTranslate.slnx`
- 测试：`dotnet run --project tests/SelectTranslate.Core.Tests --no-build`
- 发布：`dotnet publish src/SelectTranslate.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`（完整参数见 `docs/ops/release.md`）
- 本机调试：`powershell -File tools/deploy.ps1`（构建 → 部署到 `%LOCALAPPDATA%\SelectTranslate\app` → 刷新桌面快捷方式）
- 发布打包：`powershell -File tools/publish.ps1`（产物在 `dist/`，含 SHA256）

注意：

- 若 exe 落在低完整性/受限目录（例如某些沙箱工作区），启动后进程会被降权：写不了用户目录、托盘图标会被 UIPI 拦掉、收不到中键。用 `tools/deploy.ps1` 部署到 `%LOCALAPPDATA%`，或使用发布包；程序检测到低完整性会弹窗说明。
- 诊断取词：`SelectTranslate.exe --probe-selection out.txt` 会把当前前台窗口的选区写到文件（排查 UIA/剪贴板取词用）。
- 受限环境排查界面：`SelectTranslate.exe -portable --allow-low-integrity` 跳过低完整性守卫（只为验证 XAML/DI 能否构造，日常不要用）。
- 卡片预览：SelectTranslate.exe --render-card card.png 把卡片渲染成 PNG（验证配色与排版，不弹窗）。
- 测试工程用 **xunit v3**（进程内运行器，`OutputType=Exe`），不要用 `dotnet test`——VSTest testhost 在受限会话里会因无法打开父进程句柄而中止。
- 测试的临时目录放在 `bin/.../test-data`，不要用 `%TEMP%`（受限会话会拒绝创建）。
- `dotnet` 首次运行需要在用户目录（`~/.dotnet`、`~/.nuget`）写配置；首次执行若被沙箱拒绝，用完全权限跑一次即可，之后默认权限可用。

## 约定

- 文档一律简体中文；代码、标识符、提交信息用英文。
- 文档分层不可越界：**PRD 不写技术**（类名、Win32 API、配置键出现即错层），**design 不写需求**（不新增用户可见行为）。
- 需求编号三向可追溯：`FR-xx`（PRD）↔ `REQ-xx.y`（requirements）↔ `AC-xx`（验收）。
- 任何行为变更：先改 PRD 与 requirements，再改 design 与 tasks；决策变更追加 ADR，旧 ADR 不修改。
- 文档与代码同仓、同提交；规格过期比没有规格更糟。

## 红线

1. 不擅自扩大范围：`docs/product/PRD.md` §3 的非目标清单是硬边界，新增需求必须由用户确认。
2. 不在文档里写入真实 API Key、令牌、内网地址。
3. 不引入新平台/新语言/新框架；技术栈变更必须走 ADR 并由用户批准。
4. 不跳过规格直接写代码；`tasks.md` 之外的工作视为计划外。
5. 不删除或重写历史 ADR；结论变了就新增一条并标注取代关系。

## 协作方式

- 每次会话开始：先读本文件 → 再读当前任务对应的 `requirements.md` 条目 → 再读 `design.md` 相关章节。
- 实现一个任务：只动 `tasks.md` 中已列出的范围；完成后勾选，并把偏离写回文档。
- 用户说"继续"时：从 `tasks.md` 第一个未勾选项开始。