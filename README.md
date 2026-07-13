# WinUIMacro

WinUIMacro 是一款面向 Windows 的轻量级键鼠宏工具，可以录制、编辑和回放键盘与鼠标操作，并通过全局按键或鼠标侧键触发宏。

## 功能概览

- 录制键盘、鼠标按键和滚轮操作
- 编辑录制序列并调整回放模式
- 支持单次、按住和切换三种回放模式
- 使用全局键盘按键或鼠标侧键触发宏
- 关闭主窗口后继续在系统托盘运行
- 将宏与触发绑定保存在程序目录，便于便携使用和备份

## 用户指南

### 系统要求

- Windows 10 1809（内部版本 17763）或更高版本
- x64 处理器与操作系统
- [Microsoft Visual C++ 可再发行组件（x64）](https://learn.microsoft.com/zh-cn/cpp/windows/latest-supported-vc-redist)

### 选择发布包

当前版本提供两种非打包的便携式发布包：

| 发布包 | 文件名 | 运行时要求 |
| --- | --- | --- |
| 非自包含版 | `WinUIMacro-1.0.0-win-x64.7z` | 需要安装 [.NET 10 Desktop Runtime（x64）](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0) 和 [Windows App Runtime 2.2（x64）](https://learn.microsoft.com/zh-cn/windows/apps/windows-app-sdk/downloads) |
| 自包含版 | `WinUIMacro-1.0.0-win-x64-self-contained.7z` | 已包含所需的 .NET 和 Windows App SDK 运行时，体积较大 |

### 安装与启动

1. 下载适合自己的发布包。
2. 将压缩包完整解压到一个具有写入权限的目录。不要直接在压缩包内运行，也不建议解压到 `Program Files`。
3. 运行目录中的 `WinUIMacro.exe`。

关闭主窗口后，程序会继续在系统托盘中运行。可以通过托盘图标重新打开主窗口或彻底退出程序。

### 创建和录制宏

1. 在宏列表中创建一个宏。
2. 根据需要选择回放模式。
3. 点击“开始录制”，然后执行需要记录的键盘和鼠标操作。
4. 点击录制按钮所在的停止区域结束录制，也可以返回宏列表或关闭窗口来停止录制。
5. 检查录制序列并保存宏。

录制期间产生的操作只会在保存后持久化。离开存在未保存修改的宏时，程序会询问是否保存或放弃修改。

### 设置触发键

1. 打开“按键绑定”页面。
2. 选择一个支持的键盘按键或鼠标侧键。
3. 选择要绑定的已保存宏。

每个触发键只能绑定一个宏。删除宏时，与该宏关联的触发绑定也会被永久解除。

### 回放模式

| 模式 | 行为 |
| --- | --- |
| 单次 | 按下触发键后完整执行一次 |
| 按住 | 按住触发键时循环执行，松开后停止 |
| 切换 | 第一次按下开始循环，再次按下同一触发键停止 |

回放被停止或程序退出时，WinUIMacro 会尝试释放由宏保持按下状态的键盘按键和鼠标按键。

### 数据、备份与升级

所有用户数据都保存在程序目录下：

```text
Data\Macro\
```

宏以 JSON 文件保存，触发绑定保存在 `bindings.toml` 中。

- 备份：退出 WinUIMacro 后复制整个 `Data` 目录。
- 恢复：退出 WinUIMacro 后，将备份的 `Data` 目录复制回程序目录。
- 升级：解压新版本并保留原有 `Data` 目录。不要在没有备份的情况下删除旧程序目录。
- 卸载：从托盘退出程序，然后删除程序目录。删除 `Data` 目录会同时删除所有宏和绑定。

程序遇到损坏或不受支持的宏文件时会跳过该文件并显示错误，不会主动删除原文件。

### 使用提醒

- WinUIMacro 会在后台接收全局键鼠输入，并使用 Windows `SendInput` API 执行宏。
- 请勿录制密码、支付信息、恢复密钥等敏感内容。
- 某些游戏、远程桌面、安全软件或以更高权限运行的程序可能阻止输入录制或注入。
- 请先在非关键环境中确认宏行为，避免错误操作造成数据丢失。

## 开发者指南

### 开发环境

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- WinUI 3 所需的 Windows SDK 和开发组件
- [7-Zip](https://www.7-zip.org/)，用于生成发布压缩包

发布目标会自动查找以下位置：

```text
C:\Program Files\7-Zip\7z.exe
C:\Program Files (x86)\7-Zip\7z.exe
```

如果 7-Zip 位于其他目录，可以在发布命令后追加：

```powershell
-p:SevenZipExecutable="D:\Tools\7-Zip\7z.exe"
```

### 还原、构建与测试

```powershell
dotnet restore WinUIMacro.slnx
dotnet build WinUIMacro.slnx -p:Platform=x64
dotnet test WinUIMacro.Engine.Tests\WinUIMacro.Engine.Tests.csproj -p:Platform=x64
```

### 发布

| 发布配置 | 部署方式 | 发布目录 | 压缩包 |
| --- | --- | --- | --- |
| `win-x64` | 非自包含 | `publish` | `WinUIMacro-1.0.0-win-x64.7z` |
| `win-x64-self-contained` | 自包含 | `publish-self-contained` | `WinUIMacro-1.0.0-win-x64-self-contained.7z` |

生成非自包含发布包：

```powershell
dotnet publish WinUIMacro\WinUIMacro.csproj -c Release -p:Platform=x64
```

生成自包含发布包：

```powershell
dotnet publish WinUIMacro\WinUIMacro.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64-self-contained
```

两种配置生成的 7z 文件都会保存在仓库根目录的 `artifacts` 目录中，彼此不会覆盖。

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
