# DustDesk

一个 Windows 本地桌面管理工具，用来整理桌面、记录工作、管理项目、快速启动常用内容，并提供桌面搜索组件。

> 基于 C# / .NET 8 / WinForms 开发，数据保存在本地。

## 功能特性

- 桌面收纳：扫描桌面文件，分类管理，支持拖拽整理。
- 桌面组件：桌面收纳、工作记录、项目管理、快捷启动、搜索组件可独立显示。
- 快速搜索：桌面胶囊搜索框，输入即显示结果，双击打开文件。
- 工作记录：管理待办事项、备注和完成状态。
- 便签：本地便签编辑，支持自动保存。
- 项目管理：按项目、阶段、事项管理进度和关联路径。
- 快捷启动：统一管理常用软件、文件夹和文件。
- 设置中心：管理组件显示、透明配色、快捷键和基础设置。

## 下载使用

普通用户建议下载 GitHub Releases 里的压缩包：

1. 打开仓库右侧的 `Releases`。
2. 下载 `DustDesk-release.zip`。
3. 完整解压压缩包。
4. 双击 `DustDesk.exe` 运行。

发布包是 Windows x64 自包含版本，不需要额外安装 .NET 运行库。

## 本地开发

需要安装：

- Windows 10 或更高版本
- .NET 8 SDK

运行项目：

```powershell
dotnet run
```

## 打包发布

生成解压即用的 Windows x64 自包含版本：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
Compress-Archive -Path "release\*" -DestinationPath "DustDesk-release.zip" -Force
```

## 目录说明

```text
DustDesk/
├─ images/              界面图标和组件资源
├─ Data/                本地用户数据，不提交到 GitHub
├─ AppStore.cs          数据读写
├─ MainForm.cs          主界面和主要功能
├─ Models.cs            数据模型
├─ NativeGlass.cs       Windows 原生窗口效果
├─ Program.cs           程序入口
└─ DustDesk.csproj      项目配置
```

## GitHub 提交建议

以下内容不建议提交：

- `Data/`
- `bin/`
- `obj/`
- `.tmp/`
- `release/`
- `*.zip`

发布包请通过 GitHub Releases 上传，不要直接提交到源码仓库。

## 反馈

问题反馈和咨询：抖音 `Aby081298`
