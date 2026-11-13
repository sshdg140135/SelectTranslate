# ADR-0003：翻译服务商采用统一适配器接口

- 状态：已接受
- 日期：2026-11-04
- 相关：`design.md` §5、`requirements.md` FR-11

## 背景

v1.0 只对接 OpenAI 兼容接口，但明确预期后续接入 Google 翻译、有道翻译等非兼容协议。可选做法：

| 方案 | 说明 | 问题 |
|---|---|---|
| A. 直接写一个 `OpenAiClient` | 最快 | 后续每加一家都要改 UI、状态机与配置；技术债集中在主流程 |
| B. 现在就实现多家 | 一步到位 | 需求未确认，v1.0 范围膨胀，且没有真实账号验证 |
| C. 统一接口 + 注册表，v1.0 只实现一家 | 预留边界，按需扩展 | 初期多一层抽象，代码量略增 |

## 决策

采用 **C**：

- 定义 `ITranslationProvider`（Id、DisplayName、Capabilities、TranslateAsync），以 `TranslationChunk` 统一流式/非流式差异。
- `ProviderRegistry` 按 `api.providerType` 解析实现；设置页下拉由注册表驱动。
- v1.0 只注册 `openai-compatible`；Google、有道在 v1.1 以新增实现类的方式接入，UI 与状态机零改动。
- 错误统一映射为 `TranslationErrorKind`，由 UI 层出文案。

## 理由

1. 接口边界已经清楚（一次请求、流式回调、取消），抽象成本低。
2. 避免主流程出现服务商专有字段（如 `choices[0].delta`），这是最容易腐烂的地方。
3. 可以用一个测试用实现做架构验收（AC-30），不依赖真实第三方账号。
4. 不承诺 v1.0 就支持多家，范围可控。

## 后果

- `TranslationRequest` / `TranslationChunk` / `ProviderCapabilities` 成为稳定契约，改动需评审。
- 非流式服务商（Google/有道）需要一次性回传整段，UI 必须能处理"一个 chunk 就是全文"。
- 配置需要预留 `api.providerType`，并为将来的多档案（`providers[]`）留扩展位。

## 被否决的方案

- **A 单一客户端**：短期省事，长期把服务商细节焊死在 UI 里。
- **B 立即多实现**：无真实接口验证，容易做错签名与错误码映射，且超出 v1.0 范围。