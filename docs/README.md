# 文档索引

本目录遵循轻量 SDD（规格驱动）分层：**产品说做什么，规格说怎么验收，设计说怎么做，任务说先做哪个，ADR 说为什么**。任何一层都不替另一层说话。

## 阅读顺序

1. [`product/PRD.md`](product/PRD.md) —— 产品需求（what / why / 范围）
2. [`specs/001-mvp/requirements.md`](specs/001-mvp/requirements.md) —— 可测试需求（EARS）与验收（AC）
3. [`specs/001-mvp/design.md`](specs/001-mvp/design.md) —— 技术设计（how）
4. [`specs/001-mvp/tasks.md`](specs/001-mvp/tasks.md) —— 任务与进度
5. [`adr/`](adr/) —— 决策记录
6. [`ops/release.md`](ops/release.md) —— 打包与分发

辅助材料：[`product/glossary.md`](product/glossary.md)（术语）、[`product/competitive-research.md`](product/competitive-research.md)（竞品调研）、[`specs/001-mvp/research.md`](specs/001-mvp/research.md)（技术验证记录）。

## 每层只回答一个问题

| 层 | 文件 | 必须包含 | 禁止出现 |
|---|---|---|---|
| 产品 | `product/PRD.md` | 用户、场景、可观察行为、范围、指标 | 类名、Win32 API、配置键、协议细节 |
| 规格 | `specs/*/requirements.md` | 带编号、可测试的 REQ 与 AC | 实现方案、架构选择 |
| 设计 | `specs/*/design.md` | 架构、时序、接口、数据、风险 | 新增用户可见行为 |
| 任务 | `specs/*/tasks.md` | 可勾选任务、依赖、对应 AC | 需求讨论、方案争论 |
| 决策 | `adr/NNNN-*.md` | 背景、选项、结论、后果 | 修改历史结论 |

## 变更规则

- 行为变更：PRD → requirements → design → tasks，顺序不能反。
- 技术选型变更：新增 ADR，标注取代哪一条；旧 ADR 保持原样。
- 文档与代码同一提交；代码合入前，相关 AC 必须能逐条勾选。
- 迁移或重命名文档时，同步更新 `AGENTS.md` 与本文档索引。

## 编号约定

- `FR-xx`：产品功能编号，稳定不变，删除功能时保留空号。
- `REQ-xx.y`：需求条目，`xx` 对应 FR，`y` 为该功能下的第几条。
- `AC-xx`：验收用例，与 REQ 多对多，编号全局唯一。
- `ADR-NNNN`：四位递增，永不复用。

## 版本

| 版本 | 日期 | 说明 |
|---|---|---|
| v1.0 | 2026-11-04 | 从单文件《需求文档》拆分为分层文档，确立轻量 SDD 体系 |