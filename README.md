# MC Mod Migrator

## 给普通用户的发布包（推荐）

不要把 `start.bat` / `start.command` 发给普通玩家，它们是开发者调试入口。运行 `scripts/package-electron.ps1` 后会得到可直接分发的 Electron 桌面应用：Windows 用户双击 `MC Mod Migrator.exe`，macOS 用户打开 `Electron.app`。发布包自带运行时，因此 **不要求用户安装 Node.js、Java 或 Python**，并且目录选择会使用应用原生文件对话框。

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\package-electron.ps1 -Platform win32 -Architecture x64
```

### Windows 原生免 Node 版

已编译的 `release/MC Mod Migrator.exe` 是 Windows 原生桌面程序：双击运行后直接用“浏览...”选择两个 `mods` 文件夹，不启动浏览器、不需要 Node.js。它使用 Windows 自带的 .NET Framework 4.x（Windows 10/11 通常已包含）；重新编译脚本在 `native-windows/build.bat`。

迁移结束后，Windows 原生版会将完整日志自动写入 exe 同级的 `logs/migration-日期-时间.log`。macOS 版按系统权限规范写入 `~/Library/Logs/MC Mod Migrator/`，完成页会显示确切路径。

脚本会下载官方 Electron 发布包；可将 `-Architecture x64` 改成 `arm64`。macOS 包同样可在 Windows 上准备：`-Platform darwin -Architecture arm64`。正式公开发布前仍建议在 macOS 上进行代码签名与公证，避免 Gatekeeper 警告。

### macOS DMG 发布包

在 **macOS** 电脑上执行以下脚本，即可生成“拖入应用程序文件夹”的 DMG；最终用户不需要安装 Node.js。Apple Silicon 使用 `arm64`，Intel 使用 `x64`：

```bash
chmod +x scripts/package-macos.sh
./scripts/package-macos.sh arm64
```

输出为 `dist/MC Mod Migrator-macos-arm64.dmg`。公开分发前应在 Mac 上对应用签名并公证，否则 Gatekeeper 会显示安全提示。

## 开发者调试方式

Windows / macOS 本地模组版本迁移器。在 Windows 双击 `start.bat`；在 macOS 首次运行前执行 `chmod +x start.command`，随后双击 `start.command`（或在终端运行 `zsh start.command`）。浏览器打开后选择来源与目标 `mods` 文件夹，再填入目标 Minecraft 版本和加载器。

如果系统的原生文件夹对话框没有出现，可以直接把 `mods` 文件夹的完整路径粘贴到对应输入框，来源路径确认后会立即开始扫描。

它会读取 JAR 内的 Fabric / Forge / NeoForge / Quilt 元数据，默认锁定加载器核心，优先从 Modrinth 下载与目标版本、加载器相符的发布文件；没有可用版本时，会递归跳过依赖它的模组。

对每个成功迁移的模组，它还会检查来源 `mods` 文件夹同级的 `config` 目录。名称或路径中能匹配模组内部 ID/名称的 JSON、TOML、CFG、CONF、Properties、TXT 文件会复制到目标实例的同级 `config` 目录。因此如 `config/tweakeroo.json` 的 Tweakeroo 快捷键会一并带走。目标已有且内容不同的配置会先保存为 `*.migrator-backup`，再写入迁移的配置。

## 注意

- 目标文件夹会写入下载的 JAR；请先备份自己的整合包。
- CurseForge 下载 API 需要个人 API key；在「可选：CurseForge API Key」中填入后，它会在 Modrinth 无结果时自动检索。未填时结果页仍会给出 CurseForge 搜索链接。
- MCMOD 没有适用于此用途的稳定公开下载 API，因此仅作为人工核验入口。
