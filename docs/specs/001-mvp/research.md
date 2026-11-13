# 技术验证与可行性记录（research）

| 项目 | 内容 |
|---|---|
| 日期 | 2026-11-04 |
| 状态 | 桌面研究完成，实机验证待 M1/M2 |
| 用途 | 记录"为什么这条路能走通"，以及哪些结论还没有实测 |

本文件只记录**技术事实与验证结论**，不重复需求与设计。结论变化时更新本文件并注明日期。

## 1. 已确认（有据可查）

### 1.1 技术栈可行性

- `.NET 10` 为 LTS 版本，支持 `net10.0-windows` 目标框架与 WPF。
- 自包含单文件发布（`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + 压缩）在 .NET 6+ 已成熟，WPF 应用可产出单个 exe。
- 预计体积 60–90MB（含运行时）；`EnableCompressionInSingleFile=true` 可显著压缩。
- 目标机器为 Windows 11 22H2+，满足 WPF 与 DWM 新属性（圆角、Mica/Acrylic）的最低要求。

### 1.2 全局鼠标钩子

- `WH_MOUSE_LL` 为全局低级钩子，可拦截中键按下/抬起；回调运行在安装钩子的线程，必须有消息泵。
- 系统对低级钩子有超时限制（默认约 300ms，注册表 `LowLevelHooksTimeout` 可调），超时会被跳过甚至摘除，因此回调内不能有阻塞操作。
- 通过 `GetAsyncKeyState(VK_CONTROL)` 可在回调内判断修饰键，实现 Ctrl 旁路。

### 1.3 取词

- UI Automation `TextPattern.GetSelection()` 是读取选区文本的标准路径；`TextPattern2` 提供额外能力。
- UIA 调用在目标进程无响应时可能长时间阻塞，必须设超时并隔离线程（专用 STA）。
- `IsPassword` 属性可识别密码框，必须在使用前检查。
- 对不支持 UIA 的应用，`SendInput(Ctrl+C)` + 剪贴板读取是通用兜底；`GetClipboardSequenceNumber` 可用于等待复制完成。
- 剪贴板还原需自行枚举并保存格式（文本/HTML/RTF/位图/文件），WPF `Clipboard` 类不便于精细控制。

### 1.4 非激活浮窗

- `WS_EX_NOACTIVATE` + `ShowActivated=false` 可实现"显示但不抢焦点"；配合 `WS_EX_TOOLWINDOW` 可从 Alt-Tab 隐藏。
- Windows 11 22H2+ 支持 `DWMWA_WINDOW_CORNER_PREFERENCE`（圆角）与 `DWMWA_SYSTEMBACKDROP_TYPE`（Mica/Acrylic）；远程桌面或旧驱动下可能失败，需回退。
- 多屏混合 DPI 定位应使用物理像素（`SetWindowPos`）并按目标显示器 DPI 换算，避免 WPF 逻辑像素导致的偏移。

### 1.5 密钥与配置

- `System.Security.Cryptography.ProtectedData`（DPAPI，`CurrentUser` 范围）可在无管理员权限下加密字符串；换 Windows 账户无法解密。
- 配置写盘应采用"临时文件 + 原子替换"，防止断电损坏。

### 1.6 服务商协议

- OpenAI Chat Completions 的流式为 SSE：逐行 `data: {json}`，以 `data: [DONE]` 结束。
- DeepSeek、OpenAI、OpenRouter、SiliconFlow、vLLM、Ollama、LM Studio 均提供 OpenAI 兼容端点，差异主要在 Base URL、模型名与鉴权头。
- Google Cloud Translation v3 与有道智云为非 OpenAI 协议，需要独立适配器（v1.1）。

## 2. 待实机验证（M1/M2 的 spike）

| 编号 | 假设 | 验证方法 | 失败时的备选 |
|---|---|---|---|
| V1 | 单文件 exe 在干净 Win11 上双击即启，无运行时提示 | 干净虚拟机解压运行 | 改用文件夹版发布 |
| V2 | 单文件冷启动 ≤2s | 秒表 + 三次取中位数 | ReadyToRun；或启动时预热 |
| V3 | 钩子回调 <10ms，连续使用 8 小时不被摘除 | 压测 + 日志 | 定时重建；改用 Raw Input |
| V4 | UIA 在 Chrome/Word/VS Code 覆盖率 ≥80% | 兼容性清单逐项实测 | 提高 Ctrl+C 兜底占比 |
| V5 | 非激活卡片在 Chrome/Word 中不抢焦点 | 显示后立即在原应用输入 | `SetForegroundWindow` 还原焦点 |
| V6 | 混合 DPI 双屏定位误差 ≤2px | 150%+100% 双屏实测 | 全物理像素重算 |
| V7 | 剪贴板还原对富文本/图片无损 | Word/网页复制后翻译再粘贴 | 只还原纯文本；提供关闭开关 |
| V8 | Acrylic 在远程桌面下不异常 | RDP 会话测试 | 检测到 RDP 直接用纯色 |

## 3. 已知风险与研究结论

- **Electron/Chromium 的 UIA 可访问性不稳定**：VS Code、Discord 等应用在未开启可访问性时可能读不到选区，Ctrl+C 兜底是必需的。
- **全屏独占游戏**：低级钩子仍能收到事件，但取词基本不可用；产品上引导加入黑名单。
- **管理员窗口**：未提权的进程无法对提权窗口做 UIA/注入式复制；只能提示用户提权运行。
- **SmartScreen**：未签名的新 exe 会触发"未知发布者"，这是分发阶段的主要摩擦，需在 README 中说明。
- **杀软误报**：全局钩子 + 自包含 exe 容易被启发式扫描，需要提供 SHA256 与（后续）签名。

## 4. 参考

- 低级鼠标钩子：https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc
- 钩子超时与 `LowLevelHooksTimeout`：https://learn.microsoft.com/windows/win32/winmsg/about-hooks
- UI Automation TextPattern：https://learn.microsoft.com/dotnet/api/system.windows.automation.textpattern
- DWM 窗口属性：https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- 单文件发布：https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
- DPAPI：https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata
- OpenAI Chat Completions：https://platform.openai.com/docs/api-reference/chat
- Google Cloud Translation：https://cloud.google.com/translate/docs
- 有道智云：https://ai.youdao.com/