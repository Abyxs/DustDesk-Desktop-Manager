# DustDesk

DustDesk 是一个 Windows 本地桌面管理工具，基于 C#、.NET 8 和 WinForms 开发。

## 功能

- 桌面收纳：扫描桌面文件，分类整理，支持桌面组件。
- 工作记录：记录待办事项，支持桌面显示。
- 便签：本地便签编辑和自动保存。
- 项目管理：按项目、阶段、事项管理进度和路径。
- 快捷启动：管理常用软件、文件和文件夹。
- 搜索组件：桌面胶囊搜索框，支持快速检索和打开文件。
- 设置中心：管理桌面组件显示、快捷键和基础设置。

## 本地运行

需要安装 .NET 8 SDK。

```powershell
dotnet run
```

## 打包发布

生成 Windows x64 自包含版本，用户解压后可直接运行，不需要额外安装 .NET。

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
Compress-Archive -Path "release\*" -DestinationPath "DustDesk-release.zip" -Force
```

## GitHub 发布方式

源码仓库不要提交 `Data/`、`bin/`、`obj/`、`release/` 和 `*.zip`。

发布给用户时，在 GitHub Releases 新建版本，把 `DustDesk-release.zip` 上传为附件。

用户下载 ZIP 后，完整解压并双击 `DustDesk.exe` 即可使用。

## 数据说明

程序运行时会在 `Data/` 目录保存本地配置和内容。该目录属于用户数据，不建议提交到 GitHub。
