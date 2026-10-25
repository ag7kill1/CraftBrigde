# 把 CraftBridge 放到 GitHub

GitHub 不要求源码必须包含 `.github` 文件夹。它保存 GitHub 平台使用的配置，不决定项目是不是开源；CraftBridge 的开源许可由根目录的 `LICENSE` 声明。

## 这些文件各做什么

| 文件或目录 | 用途 |
|---|---|
| `README.md` | 仓库首页显示的项目介绍、使用与构建说明。 |
| `LICENSE` | MIT 许可证，说明其他人如何使用、修改和分发项目。 |
| `.gitignore` | Git 提交时忽略构建输出、环境配置、密钥和临时文件。 |
| `.gitattributes` | 规定文本换行和二进制文件处理方式。 |
| `.github/workflows/ci.yml` | 提交代码或打开 PR 后，自动在 Windows 构建并运行测试。 |
| `.github/ISSUE_TEMPLATE/` | 创建 Issue 时提供问题反馈和功能建议表单。 |
| `.github/PULL_REQUEST_TEMPLATE.md` | 提交 PR 时提醒贡献者说明改动及验证结果。 |
| `CONTRIBUTING.md` | 简短的参与贡献指南。 |

`.git` 与 `.github` 是不同目录：`.git` 由 Git 在本地管理仓库历史，无需手工编写，也不放进源码 ZIP；`.github` 是可以随源码提交的平台配置。

## 第一次上传

1. 解压 GitHub 源码包。解压后的根目录应直接有 `README.md`、`LICENSE`、`.github/`、`src/` 等内容。
2. 在 GitHub 创建名为 `CraftBridge` 的 Public 仓库。源码包已带 README、许可证和忽略文件，创建时这些初始化选项可以留空。
3. 进入新仓库的上传文件页面，上传解压后的文件和目录，然后提交。不要只把源码 ZIP 当作一个文件上传；那样 GitHub 不会直接展示其中的源码。
4. 确认 `.github/`、`.gitignore` 和 `.gitattributes` 也已上传；这些以点开头的名称需要保持原样。
5. 打开仓库的 **Actions** 页面查看 `Windows build and tests`。成功后可在本次运行的 Artifacts 中下载程序与测试报告。

如果上传页面让你选择整个文件夹，确认没有在仓库根目录额外套上一层项目文件夹，否则工作流的默认构建路径会找不到 `build.ps1`。

## 自动测试会做什么

工作流在 push、pull request 或手动启动时运行 `build.ps1 -Test`，保存构建结果和测试报告 14 天。它使用 GitHub 托管的 Windows 运行环境，不需要填写个人访问令牌或其他 Secrets。

本次交付已校验工作流及 Issue 表单的 YAML，并在本地验证过其构建命令。GitHub 上的首次运行需要上传仓库后确认；本地通过不等于已经远程运行成功。

给玩家长期下载的发行程序可以放在 GitHub 的 **Releases** 中；Actions 中的构建产物用于查看某次检查结果。

## 官方说明

- [GitHub 工作流语法](https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions)
- [Issue 表单配置](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/configuring-issue-templates-for-your-repository)
- [PR 模板](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/creating-a-pull-request-template-for-your-repository)
