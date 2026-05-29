# CDiskManager

一款用于分析与清理 Windows C 盘空间的桌面工具，基于 **WinUI 3 / .NET 8** 构建，采用 MVVM 架构。

## 功能

- **概览**：以环形图与卡片展示各分区使用情况，按使用率进行颜色分级（绿/黄/红）。
- **空间扫描**：递归扫描所选分区，按占用大小排序展示文件夹/文件，支持面包屑导航逐级下钻。
- **垃圾清理**：检测并清理临时文件、Windows Update 缓存、预读取、缩略图、浏览器缓存、系统日志、传递优化与回收站，清理前二次确认。
- **大文件检测**：按可配置的阈值查找大文件，支持批量选择、打开所在位置、删除。
- **重复文件检测**：基于「大小 → 部分哈希 → 完整 SHA256」三级比对，准确识别内容相同的文件，默认保留最早的副本。
- **分区建议**：分析 C 盘中的用户文件夹，给出迁移到其他分区的建议。
- **设置**：主题切换（跟随系统/浅色/深色）、默认扫描分区、扫描阈值、删除到回收站开关，配置持久化到本地。

## 安全设计

- 删除操作默认进入**回收站**，可在设置中切换为永久删除。
- 所有删除/清理操作均有确认对话框。
- 受保护的系统目录需要管理员权限，应用会在需要时提示「以管理员身份重启」。

## 技术栈

- WinUI 3（Windows App SDK 1.5）
- .NET 8（`net8.0-windows10.0.22621.0`，x64）
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection

## 构建与运行

```pwsh
dotnet build CDiskManager/CDiskManager.csproj -c Debug -p:Platform=x64
```

生成的可执行文件位于：

```
CDiskManager/bin/x64/Debug/net8.0-windows10.0.22621.0/win-x64/CDiskManager.exe
```

## 项目结构

```
CDiskManager/
├── Models/        数据模型
├── Services/      扫描、清理、重复检测、分区分析、设置等服务
├── ViewModels/    各页面的视图模型（MVVM）
├── Views/         各页面的 XAML 与代码后置
├── Helpers/       转换器、原生互操作、管理员权限等辅助类
├── App.xaml(.cs)  应用入口与依赖注入配置
└── MainWindow     主窗口与导航
```
