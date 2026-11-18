# 发布与分发

| 项目 | 内容 |
|---|---|
| 版本 | v0.9.0（首个可分发预览版） |
| 适用范围 | 便携 zip；安装器 / 自动更新 / 签名暂不做 |
| 相关 | `design.md` §8、`PRD.md` FR-12 |

## 1. 一键打包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1
# 冷启动更快（体积 +25MB 左右）：
powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish.ps1 -ReadyToRun
```

脚本会：

1. 读取 `SelectTranslate.App.csproj` 的版本号；
2. `dotnet publish` 自包含单文件（`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + 压缩）；
3. 把 `README.md` 放进包内，删除 `.pdb`；
4. 生成 `dist/SelectTranslate-v<版本>-win-x64.zip` 与同名 `.sha256`。

当前实测：exe ≈ 66.7 MB，zip ≈ 61.2 MB。

## 2. 产物

```
dist/
├── SelectTranslate-v0.9.0-win-x64.zip
└── SelectTranslate-v0.9.0-win-x64.zip.sha256
```

zip 内结构：

```
SelectTranslate-v0.9.0-win-x64/
├── SelectTranslate.exe
└── README.md
```

## 3. 发布前自检（本机）

```powershell
# 解压到临时目录
Expand-Archive dist\SelectTranslate-v0.9.0-win-x64.zip -DestinationPath artifacts\release-test -Force

# 冒烟：启动 → 托盘/钩子就绪 → 自动退出
$exe = "artifacts\release-test\SelectTranslate-v0.9.0-win-x64\SelectTranslate.exe"
Start-Process $exe -ArgumentList '-portable','--self-test'
Get-Content "artifacts\release-test\SelectTranslate-v0.9.0-win-x64\data\logs\*.log" -Tail 5
```

通过标准：日志出现 `启动 … 0.9.0`、`托盘已就绪`、`键盘钩子已安装`、`鼠标钩子已安装`、`自检完成`，且没有 `startup-error.log`。

## 4. 分发

1. 把 zip 与 `.sha256` 一起发给对方；
2. 对方解压后先校验（可选）：`certutil -hashfile <zip> SHA256`，与 `.sha256` 内容比对；
3. 双击 `SelectTranslate.exe`；SmartScreen 提示时选"更多信息 → 仍要运行"；
4. 托盘右键 → 打开设置 → 填接口地址、API Key、模型 → 测试连接 → 保存。

## 5. 干净机器验收清单（v1.0 前必须补）

- [ ] 干净 Win11 x64 虚拟机：解压即运行，无 .NET 依赖
- [ ] 首次启动无崩溃、托盘可见、热键生效
- [ ] 配置写入 `%APPDATA%\SelectTranslate\config.json`，Key 为 DPAPI 密文
- [ ] Chrome / Edge / Word / VS Code / PDF 阅读器各取词一次
- [ ] 双屏 + 混合 DPI 定位
- [ ] 失败矩阵：错误 Key、断网、429、超时
- [ ] 卸载：退出后删除目录 + `%APPDATA%\SelectTranslate` 即干净

## 6. 版本策略

- 预览：`0.x.y`，接口与配置可能变化；
- 正式：`1.0.0` 起，配置带 `schemaVersion` 做迁移；
- 每次发版更新 `CHANGELOG.md` 与 `SelectTranslate.App.csproj` 的 `<Version>`。