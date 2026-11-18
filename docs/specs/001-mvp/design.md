# 001-mvp 技术设计（design）

| 项目 | 内容 |
|---|---|
| 版本 | v1.0 |
| 日期 | 2026-11-04 |
| 上游 | `docs/product/PRD.md`、`requirements.md` |
| 范围 | v1.0（M1–M4）的实现方案 |

本文件回答"怎么做"。不新增用户可见行为；若发现需求缺口，回到 `requirements.md` 补充，而不是在这里悄悄实现。

## 1. 技术栈与总体架构

| 项 | 选择 |
|---|---|
| 语言/框架 | C# + WPF |
| 运行时 | .NET 10 LTS（自包含发布，用户无需安装） |
| 目标 | `net10.0-windows`，`win-x64` |
| 发布形态 | 单文件 exe（`PublishSingleFile` + 压缩），可选文件夹版 |
| 依赖 | CommunityToolkit.Mvvm、Microsoft.Extensions.DependencyInjection、System.Security.Cryptography.ProtectedData |
| 测试 | xUnit v3（进程内运行器，`dotnet run --project tests/...`；覆盖配置、语言检测、SSE 解析） |

```
┌─────────────────── SelectTranslate.exe（WPF / .NET 10 / x64）───────────────────┐
│                                                                                  │
│  TrayService ── SettingsWindow ── PopupWindow（非激活 / 置顶 / DWM 圆角）        │
│       │                │                    ▲                                    │
│       ▼                ▼                    │ 状态与文本（Dispatcher）            │
│  ConfigService      AppHost（DI / 生命周期）│                                    │
│  （DPAPI）                │                 │                                    │
│       ▲                  ▼                 │                                    │
│       └──── TranslationClient ── ProviderRegistry ── ITranslationProvider        │
│                                    ├─ OpenAiCompatibleProvider（v1.0）           │
│                                    └─ Google / Youdao 适配器（v1.1，预留）       │
│                          ▲                                                       │
│  HookThread（WH_MOUSE_LL 消息泵）── SelectionService（UIA / Ctrl+C / 剪贴板）   │
│                                                                                  │
└──────────────────────────────────┬───────────────────────────────────────────────┘
                                   │ HTTPS（OpenAI 兼容；后续可扩展其他协议）
                                   ▼
                     DeepSeek / OpenAI / OpenRouter / 内网 vLLM / Ollama
```

### 1.1 工程结构

```
SelectTranslate.sln
├── src/
│   ├── SelectTranslate.App/      # WPF 外壳：窗口、托盘、宿主
│   └── SelectTranslate.Core/     # 无 UI：取词抽象、翻译、配置、语言检测、日志
├── tests/
│   └── SelectTranslate.Core.Tests/
└── docs/
```

## 2. 线程模型

| 线程 | 职责 | 约束 |
|---|---|---|
| UI 线程（WPF Dispatcher） | 窗口、托盘、设置、卡片渲染 | 不做网络与 UIA 调用 |
| 钩子线程 | 安装 `WH_MOUSE_LL`（卡片可见时另装 `WH_KEYBOARD_LL`），跑 `GetMessage` 循环 | 回调只做坐标/修饰键判断并投递事件，**<10ms 返回** |
| UIA 线程 | 执行 `TextPattern.GetSelection` | 单次 ≤400ms，超时放弃并重建 |
| 线程池 / async | HTTP、剪贴板等待、日志 | 全部可取消 |

> 低级钩子受 `LowLevelHooksTimeout` 约束，回调超时会被跳过甚至摘除，因此钩子回调内禁止任何 I/O 与等待。

## 3. 触发与钩子

### 3.1 中键拦截

- 回调收到 `WM_MBUTTONDOWN`：若 `GetAsyncKeyState(VK_CONTROL)` 按下 → `CallNextHookEx` 放行；否则返回 `1` 吞掉，并吞掉配对的 `WM_MBUTTONUP`。
- 只处理按下，**滚轮、中键拖动一律放行**。
- 命中后 `Post` 到 UI 线程异步取词，回调立刻返回。
- 长期运行：每 10 分钟重建钩子，系统从睡眠/锁屏恢复后重建，记录日志（防止被系统静默摘除）。

### 触发源（M3 先行）

- 触发源有两个，可并行使用：**鼠标中键（默认）** 与 **可配置键盘组合**（如 `Ctrl+Alt+T`）。
- 键盘组合由低级键盘钩子匹配：`HotkeyParser` 负责解析/格式化（至少一个修饰键），匹配成功后**吞掉该键的按下与抬起**，避免前台应用收到半次组合键；未匹配时原样放行。
- 键盘触发没有鼠标坐标，锚点取当前光标位置（`GetCursorPos`），后续取词/卡片/翻译流程与中键完全一致。
- 热键匹配在钩子线程完成，只做"是否命中"的判断；真正的取词与 UI 操作投递回 UI 线程。

### 3.2 键盘钩子（仅卡片可见时）

- 卡片显示时安装 `WH_KEYBOARD_LL`，仅拦截 `VK_ESCAPE`；卡片关闭立即卸载。
- 其他按键一律放行，避免影响用户输入。

### 3.3 单实例

- 命名互斥体 `Local\SelectTranslate.SingleInstance`（按用户会话隔离）。
- 命名管道 `SelectTranslate.Activate` 传递"打开设置"指令，二次启动不创建新进程。

## 4. 取词

### 4.1 时序

```
中键按下（已拦截）
  ├─ 记录鼠标坐标、前台窗口句柄、进程名
  ├─ 黑名单 / 自身窗口 / 提权检测 → 命中则返回
  ├─ UIA：400ms 超时
  │    ├─ IsPassword → E_PASSWORD
  │    └─ GetSelection() 有文本 → 采用
  ├─ 失败 → 备份剪贴板（文本/HTML/RTF/位图/文件优先）
  ├─ SendInput(Ctrl+C)
  ├─ 等剪贴板序号变化 ≤300ms
  ├─ 读取 CF_UNICODETEXT
  └─ 还原剪贴板（尽力，失败仅告警）
```

### 4.2 实现要点

- `AutomationElement.FocusedElement` → `TextPattern` / `TextPattern2.GetSelection()`。
- UIA 调用可能挂起：放到专用 STA 线程，超时后放弃该线程并重建。
- 提权检测：`OpenProcessToken` 失败或 UIA 抛 `ElementNotAvailableException` → 判定为高权限窗口，返回 `E_ELEVATED`。
- 剪贴板统一走 Win32（`OpenClipboard` / `GetClipboardData`），不用 WPF `Clipboard` 类，以便枚举格式与保留序列号语义。
- 等待剪贴板变化：`GetClipboardSequenceNumber` + 20ms 轮询；P1 可加 `WM_CLIPBOARDUPDATE` 监听降低延迟。
- 文本清洗：Trim、去 U+200B/U+FEFF、连续 3 个以上换行压成 2 个。
- 实现偏差（M1.5）：`TextPatternRange` 在 .NET 10 的引用程序集里取不到，改用反射调用 `GetSelection` / `GetText`（模式 id 10014）。
- 实现偏差（M1.5）：剪贴板备份/还原暂用 WPF `Clipboard`（保留原 `IDataObject` 尽力还原）；Win32 全格式枚举留到 M2 与 AC-07 一起做。
- 低完整性守卫：启动时读令牌完整性级别，低完整性直接弹窗说明并退出（低完整性进程无法写用户目录、托盘会被 UIPI 拦掉、收不到中键）。
- 总预算 800ms：UIA 400ms + 剪贴板 300ms + 余量。

### 4.3 已知限制

- DirectX 独占全屏、部分 Java/UWP、部分自绘控件可能取不到词。
- 复杂剪贴板格式（自定义格式、延迟渲染）还原后可能丢失。
- 管理员窗口需本程序同样提权才能取词。

## 5. 翻译客户端与服务商抽象

### 5.1 统一接口

```csharp
public interface ITranslationProvider
{
    string Id { get; }                          // "openai-compatible" / "google" / "youdao"
    string DisplayName { get; }
    ProviderCapabilities Capabilities { get; }  // SupportsStreaming / SupportedTargets / MaxChars
    Task TranslateAsync(TranslationRequest request,
                        IProgress<TranslationChunk> onChunk,
                        CancellationToken ct);
}
```

- `TranslationRequest`：原文、源语言（默认 auto）、目标语言、温度、超时。
- `TranslationChunk`：增量文本；非流式服务商一次性回传一个 chunk，UI 无需区分。
- 错误统一为 `TranslationErrorKind`：`Auth` / `Model` / `RateLimit` / `Network` / `Timeout` / `BadResponse` / `Internal`，由 UI 映射为中文文案。

### 5.2 注册与解析

- `ProviderRegistry` 按 `api.providerType` 从 DI 容器解析实现，并缓存。
- v1.0 只注册 `openai-compatible`；新增实现不影响 UI 与状态机。
- 设置页"服务商类型"下拉由注册表驱动：只读 `DisplayName` 与 `Capabilities`。

### 5.3 OpenAI 兼容实现

- `HttpClient` 单例；`SocketsHttpHandler`：`PooledConnectionLifetime=5min`、`AutomaticDecompression=gzip/deflate/br`。
- 请求：`POST {baseUrl}/chat/completions`，`Authorization: Bearer {key}`，`stream=true`。

提示词（M2.5 起）：

- `system` 由 `PromptBuilder` 生成：目标语言以中文名注入（如"简体中文"），规则为"只输出译文 / 保留结构 / 代码与 URL 原样 / 标识符保留原词并用括号补中文含义 / 已是目标语言才原样返回"；
- 防"回声"：译文与原文归一化后完全相同时（典型场景是只选中了 `buildBody` 这类标识符），自动追加"必须翻译、不得原样返回"的强指令重试一次；仍相同则在卡片上提示并提供「重试」，不会把原文当译文蒙混过去；
- `user` 只放原文，不再拼接"目标语言：xxx"，避免指令分散；
- 配置 `api.systemPrompt` 非空时**完全覆盖**内置模板（高级用户自定义）；空值表示使用内置模板，老版本默认文案在加载时会自动迁移为空。
- 解析：逐行 `ReadLineAsync`，取 `data:` 后的 JSON，读 `choices[0].delta.content`；`[DONE]` 结束。
- 容错：无 `data:` 前缀跳过、空块跳过、JSON 损坏记录并跳过、流中断保留已显示内容并报 `BadResponse`。
- 取消：`CancellationTokenSource` 与卡片生命周期绑定。
- 重试：网络错误/5xx/429 重试 1 次；429 读 `Retry-After` 决定提示秒数。
- M2 实现偏差：先以 `stream=false` 打通链路（整段一次回传），SSE 逐行解析在 M3 接入；`TranslationService` 已按 chunk 接口设计，UI 无需改动。

### 5.4 后续适配器（v1.1，不实现）

| 服务商 | 鉴权 | 流式 | 要点 |
|---|---|---|---|
| Google Cloud Translation v3 | API Key / OAuth2 | 否 | REST 整段返回；detect + translate 映射 |
| 有道智云 | appKey + salt + sign(SHA-256) | 否 | 签名、q 长度限制、错误码映射 |

## 6. 卡片窗口

| 属性 | 值 |
|---|---|
| 扩展样式 | `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`，`ShowActivated=false`，`Topmost=true` |
| 任务栏 | 不显示，不进入 Alt-Tab |
| 圆角 | `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)`；失败回退 1px 边框 |
| 背景 | `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW`（Win11 22H2+）；失败回退纯色 |
| 尺寸 | 宽 320（默认，300–560 可配），最小高 140，随译文向下生长；最大高 = 工作区 60%，超出内部滚动 |
| 定位 | `SetWindowPos` 物理像素；锚点光标右下 16px，越界翻转，夹进 `MonitorFromPoint` 工作区（8px 边距） |

关闭规则：

| 操作 | 未钉住 | 已钉住 |
|---|---|---|
| 点卡片外部 | 关闭 | 保持 |
| Esc | 关闭 | 关闭 |
| 卡片关闭按钮 | 关闭 | 关闭 |
| 再次中键 | 关旧卡并在新位置重开 | 同左（图钉随新内容复位） |
| 拖动卡片 | 不允许（可选中文字） | 仅卡片头部可拖动（头部按钮除外），光标保持普通箭头；拖动后内容增长只向下延伸，不再回到光标旁 |

- Esc 由独立的 `WH_KEYBOARD_LL` 键盘钩子捕获，**不拦截**按键，前台应用照常收到 Esc。
- 定位计算放在 `Core.Placement.CardPlacement`（纯函数），便于对边角/副屏做单测；窗口 DPI 变化后重新贴边。
- 提权窗口：`WH_MOUSE_LL` 钩子收不到发往更高完整性窗口的输入（UIPI），所以未提权时在任务管理器等窗口"按中键无反应"是系统行为，无法通过提示挽救；托盘提供"以管理员身份重启"（`runas`），提权重启后钩子与 UIA 都能正常工作。若钩子确实收到了事件（同等权限），仍会弹提示卡片（AC-27 / REQ-09.3）。
- 提权重启用 `--restarted-elevated` 标记，新实例启动时最多等 5 秒让旧进程释放单实例互斥体，避免"重启后什么都没剩下"。

- `SourceInitialized` 时设置扩展样式，避免首次显示抢焦点。
- 流式渲染节流 30–50ms 批量刷新，避免滚动条抖动。
- 卡片内容与状态机在 App 层；Core 层不引用 WPF。

## 7. 配置、密钥与日志

### 7.1 目录

```
%APPDATA%\SelectTranslate\
├── config.json            # 配置（Key 为 DPAPI 密文）
├── logs\
│   └── app-20261104.log
└── cache\                 # P1：模型列表等
```

便携模式（`-portable`）基目录改为 `<exe>\data\`。

服务商档案（schemaVersion 2）：

- `providers[]`：每个档案含 `id`、`name`、`providerType`、`baseUrl`、`apiKeyProtected`、`model` 等；Key 仍按档案各自 DPAPI 加密。
- `activeProviderId`：当前使用的档案；切换后下一次翻译即生效（请求时实时读取当前档案）。
- 设置页的「深度思考」下拉（默认/是/否）存的是 `thinkingMode`，请求时由 `ThinkingStrategies` 按**模型名前缀**生成对应字段（doubao/deepseek/glm/kimi → `thinking.type`；qwen → `enable_thinking`；gpt/o1/o3/o4 → `reasoning_effort`，只能降到 minimal；未识别模型不写任何字段，UI 会提示）。
- 合并顺序：思考补丁先写入请求体，`extraBodyJson` 最后覆盖，用户手填永远是最终决定。
- 每个档案可用 `extraBodyJson`（UI 里叫"附加请求参数"）向 `/chat/completions` 请求体顶层合并任意字段，用于服务商特有能力（例如火山方舟关闭深度思考 `{"thinking":{"type":"disabled"}}`）。非法 JSON 在保存时拦截；请求体合并失败按 ConfigMissing 报错。
- 调用诊断：Debug 级别会记录 `ttfb` / `total` 耗时与 `usage` 里的 prompt/completion/reasoning token 数（不记录任何文本）。
- 迁移：加载 v1 配置时，把旧的单个 `api` 节点搬进 `providers[0]` 并设为当前，随后 `api` 节点不再写出。

基目录按以下顺序选择，第一个"能创建且能写入"的生效（写一个探针文件验证）：

1. `%APPDATA%\SelectTranslate`（普通模式）或 `<exe>\data`（便携模式）
2. `%LOCALAPPDATA%\SelectTranslate`
3. `<exe>\data`

回退到第 3 项时按便携模式处理（不写开机自启），并在日志记录一行 `WARN`。三项都不可写时弹窗说明原因并退出——不允许"进程活着但托盘不存在"的静默失败。

### 7.2 配置键

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `schemaVersion` | int | 1 | 配置迁移 |
| `api.providerType` | string | `openai-compatible` | 服务商适配器 Id |
| `api.baseUrl` | string | `https://api.deepseek.com/v1` | 不含 `/chat/completions` |
| `api.apiKeyProtected` | string | 空 | DPAPI 密文 Base64 |
| `api.model` | string | `deepseek-chat` | 模型名 |
| `api.temperature` | double | 0.2 | 0–1 |
| `api.timeoutMs` | int | 30000 | 10000–120000 |
| `api.connectTimeoutMs` | int | 10000 | 3000–30000 |
| `api.stream` | bool | true | 流式开关 |
| `api.maxTokens` | int? | null | 留空由服务端决定 |
| `api.systemPrompt` | string | 空（使用内置模板） | 非空则完全覆盖内置提示词 |
| `api.extraHeaders` | object | {} | P1 |
| `target.lang` | string | `zh-CN` | 目标语言 |
| `hotkey.mouse` | string | `Middle` | `Middle`/`X1`/`X2`/`None` |
| `hotkey.keyboard` | string | 空 | 如 `Ctrl+Alt+T` |
| `hotkey.bypassModifier` | string | `Ctrl` | 旁路修饰键 |
| `hotkey.enabled` | bool | true | 总开关 |
| `behavior.interceptMiddle` | bool | true | 拦截中键 |
| `behavior.maxChars` | int | 8000 | 文本上限 |
| `behavior.restoreClipboard` | bool | true | 取词后还原 |
| `behavior.autoStart` | bool | true | 开机自启 |
| `behavior.blacklist` | string[] | [] | 进程名 |
| `behavior.escapeCloses` | bool | true | Esc 关闭 |
| `behavior.closeOnOutsideClick` | bool | true | 点外部关闭 |
| `ui.width` | int | 420 | 360–560 |
| `ui.fontSize` | int | 14 | 12–20 |
| `ui.theme` | string | `system` | `system`/`light`/`dark` |
| `ui.autoCloseSec` | int | 0 | 0 = 不自动关 |
| `ui.animate` | bool | true | 动画 |
| `ui.showModelName` | bool | true | 状态条显示模型 |
| `general.logLevel` | string | `info` | `debug` 需确认 |
| `general.portable` | bool | false | 由参数写入 |

### 7.3 密钥与容错

- `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`，Base64 后存入配置；换账户不可解密。
- 写入用"临时文件 + 原子替换"；读取失败备份为 `config.bad-{ts}.json` 并回退默认值。
- 日志：按天滚动，保留 7 天，单文件 ≤5MB；只记时间、进程名、文本长度、耗时、错误类型；不记原文/译文/Key。

## 8. 打包与分发

```powershell
dotnet publish src/SelectTranslate.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

- 产物 `SelectTranslate.exe`，预计 60–90MB（含 .NET 10 运行时与 WPF）。
- `PublishReadyToRun=true` 可改善冷启动，体积 +25–35%，可选。
- `app.manifest`：`PerMonitorV2`、`asInvoker`、声明 Win10/11 兼容。
- 分发包：exe + `README.txt` + `LICENSE` + `SHA256.txt`。

## 9. 兼容性与限制

| 应用 | 预期路径 | 备注 |
|---|---|---|
| Chrome / Edge | UIA | 少数控件退化为 Ctrl+C |
| Firefox | UIA（IA2 桥）/ Ctrl+C | 需启用 accessibility |
| Word / PowerPoint | UIA | 稳定 |
| Excel | UIA（编辑态）/ Ctrl+C（区域） | 非编辑态复制单元格值 |
| 记事本 | UIA | 稳定 |
| VS Code | UIA / Ctrl+C | Electron 可访问性不稳定时退化 |
| Windows Terminal | UIA / Ctrl+C | — |
| 微信 / QQ | Ctrl+C | 自绘控件 |
| Telegram Desktop | Ctrl+C | Qt |
| Adobe Reader / SumatraPDF | Ctrl+C | — |
| 资源管理器文件名 | 无选区 | 提示未选中 |
| 全屏独占游戏 | 通常无效 | 建议加入黑名单 |
| 密码框 / 提权窗口 | 拒绝 | 提示原因 |

## 10. 技术风险

| 风险 | 缓解 |
|---|---|
| DWM Acrylic/圆角异常（远程桌面/旧显卡） | 回退纯色 + 1px 边框 |
| 非激活窗口仍抢焦点 | 显示后把焦点还给原窗口句柄 |
| Electron/Chromium UIA 不稳定 | 直接走 Ctrl+C；P1 加 MSAA 兜底 |
| 单文件启动被杀软扫描 | 提供文件夹版备选产物 |
| 钩子被安全软件拦截 | 文档说明 + 允许改键盘快捷键 |
| 剪贴板还原失败 | 只备份常用格式；关闭还原开关 |

## 11. 附录

### 11.1 错误码

| 错误码 | 触发 | UI 文案 | 动作 |
|---|---|---|---|
| `E_NO_SELECTION` | 无选区/空 | 未检测到选中的文字 | 无 |
| `E_SELECTION_TIMEOUT` | 取词超时 | 没能读取到选中内容 | 重试 |
| `E_CLIPBOARD_BUSY` | 剪贴板占用 | 剪贴板被其他程序占用 | 重试 |
| `E_PASSWORD` | 密码框 | 已跳过密码输入框 | 无 |
| `E_NO_CONFIG` | 未配置 | 还没有配置翻译服务 | 去设置 |
| `E_AUTH` | 401/403 | API Key 无效或无权限 | 去设置 |
| `E_MODEL` | 404 | 模型不存在，请检查模型名 | 去设置 |
| `E_RATE_LIMIT` | 429 | 请求过于频繁，N 秒后重试 | 重试 |
| `E_NETWORK` | DNS/连接/证书 | 网络不可达，检查地址或代理 | 重试 |
| `E_TIMEOUT` | 超时 | 请求超时（Ns） | 重试 |
| `E_BAD_RESPONSE` | 非 JSON/空/中断 | 返回内容异常 | 重试 |
| `E_TOO_LONG` | 超限 | 文本过长，已截断 | 继续 |
| `E_ELEVATED` | 提权窗口 | 请以管理员身份运行本程序 | 关闭 |
| `E_INTERNAL` | 未预期 | 出了点问题（日志编号 xxx） | 重试/查看日志 |

### 11.2 请求示例

```http
POST {baseUrl}/chat/completions
Authorization: Bearer {apiKey}
Content-Type: application/json
```

```json
{
  "model": "deepseek-chat",
  "messages": [
    { "role": "system", "content": "你是一名专业翻译。将用户提供的文本翻译成简体中文。要求：只输出译文，保留换行与专有名词。" },
    { "role": "user", "content": "请翻译以下文本：\n\nThe quick brown fox jumps over the lazy dog." }
  ],
  "temperature": 0.2,
  "stream": true
}
```

```
data: {"choices":[{"delta":{"content":"敏捷"}}]}
data: {"choices":[{"delta":{"content":"的"}}]}
data: [DONE]
```

### 11.3 服务商兼容

| 服务商 | Base URL 示例 | 备注 |
|---|---|---|
| DeepSeek | `https://api.deepseek.com/v1` | 默认预设 |
| OpenAI | `https://api.openai.com/v1` | — |
| OpenRouter | `https://openrouter.ai/api/v1` | 模型名带前缀 |
| SiliconFlow | `https://api.siliconflow.cn/v1` | — |
| 内网 vLLM | `http://10.x.x.x:8000/v1` | 明文 HTTP，允许但告警 |
| Ollama | `http://localhost:11434/v1` | Key 可留空 |
| LM Studio | `http://localhost:1234/v1` | 同上 |

Google / 有道等非 OpenAI 兼容协议的接入计划见 §5.4 与 `docs/adr/0003-provider-abstraction.md`。