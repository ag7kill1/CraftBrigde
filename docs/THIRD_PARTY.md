# 第三方与素材说明

CraftBridge 源码及本项目自绘图标使用根目录的 [MIT 许可证](../LICENSE)。本仓库没有第三方源码副本、NuGet 包、随包运行库或第三方图标、字体文件。

## 运行环境

程序调用 Windows 已安装的 .NET Framework 4.8 标准库，包括 WinForms、GDI+、ZIP、JSON 和加密接口。源码包不包含 .NET 安装程序或系统 DLL。

界面引用系统安装的 `Microsoft YaHei UI` 和 `Segoe UI` 字体，不分发字体文件。图标由 `build.ps1` 中的几何绘图代码生成，界面图形也由本项目代码绘制。

源码包不包含 Minecraft 游戏文件、游戏存档、模组 JAR、启动器本体、账号数据或 Apple 标志。

## 设计参考

| 项目 | 本次核对的上游许可证 | 使用方式 |
|---|---|---|
| [Prism Launcher](https://github.com/PrismLauncher/PrismLauncher) | [GPL-3.0](https://github.com/PrismLauncher/PrismLauncher/blob/develop/LICENSE) | 参考实例管理与导出交互；未引入其代码、素材或二进制。 |
| [SaveState](https://github.com/Matteo842/SaveState) | [GPL-3.0](https://github.com/Matteo842/SaveState/blob/main/LICENSE) | 参考本地备份恢复思路；未引入其代码、素材或二进制。 |
| [WPF UI](https://github.com/lepoco/wpfui) | [MIT](https://github.com/lepoco/wpfui/blob/main/LICENSE) | 参考导航和主题分层思路；未依赖其库或引入其控件、图标。 |

运行格式还参考了 Fabric 和 Modrinth 的公开文档。对应链接保留在 README；CraftBridge 的迁移 ZIP 是本项目独立格式。

核对日期：2026-10-25。上游许可证属于各自项目，本项目的 MIT 声明不替代这些上游许可证。
