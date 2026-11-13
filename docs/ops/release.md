# 发布与分发

| 项目 | 内容 |
|---|---|
| 版本 | v1.0 |
| 适用范围 | 便携 zip（v1.0）；安装器/自动更新/签名见 v2.0 |
| 相关 | `design.md` §8、`PRD.md` FR-12 |

## 1. 构建

```powershell
# 清理
dotnet clean

# 发布（单文件、自包含、压缩）
dotnet publish src/SelectTranslate.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o artifacts/publish
```

可选（改善冷启动，体积 +25–35%）：

```powershell
-p:PublishReadyToRun=true
```

## 2. 产物与命名

```
SelectTranslate-v1.0.0-win-x64.zip
├── SelectTranslate.exe
├── README.txt
├── LICENSE
└── SHA256.txt
```

- 版本号遵循 `MAJOR.MINOR.PATCH`；`AssemblyInfo` 与 zip 名保持一致。
- `SHA256.txt` 内容：`<hash>  SelectTranslate.exe`。

## 3. 发布检查单

- [ ] `dotnet test` 全绿
- [ ] AC 清单在干净 Win11 虚拟机上逐条通过（至少 AC-01、AC-06、AC-12、AC-18、AC-26、AC-31）
- [ ] 首次向导在无配置环境下完整走通
- [ ] 卸载自启项后不再自启
- [ ] 日志中无原文、译文、Key
- [ ] exe 属性中的版本号与文件名一致
- [ ] README.txt 包含 3 步上手与 SmartScreen 说明
- [ ] 体积与启动时间在指标内（≤100MB、≤2s）
- [ ] 生成 SHA256 并写入发布说明

## 4. 分发说明（给接收者）

> 这个程序不需要安装。解压后双击 `SelectTranslate.exe` 即可。
> 如果 Windows 提示"已保护你的电脑"，点"更多信息"→"仍要运行"。
> 首次运行会引导你填写翻译服务的地址和 Key，填完点"测试连接"。

## 5. 便携模式

- 启动参数加 `-portable`，配置与日志写到 `<exe>\data\`。
- 便携模式默认不写开机自启，避免 U 盘盘符变化导致失效。
- 从便携模式切回普通模式：删除 `data\` 目录即可。

## 6. 回滚

- 保留上一版本 zip；出现严重问题时让用户解压旧版覆盖。
- 配置向后兼容：`schemaVersion` 升级时保留旧文件备份（`config.bad-{ts}.json`）。

## 7. 后续（v2.0）

- 安装器（MSIX 或 Inno Setup）、自动更新、代码签名、ARM64 产物。
- 签名后 SmartScreen 提示消失，README 中的"仍要运行"说明可移除。