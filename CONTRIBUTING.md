# 参与贡献

欢迎通过 Issue 反馈问题、提出建议，或提交 Pull Request。项目源码和自绘图标采用 [MIT 许可证](LICENSE)。

## 开发与验证

需要 Windows 10 / 11 和 .NET Framework 4.8。无需安装 NuGet 依赖或 .NET SDK，在项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

发行文件在 `dist/`，测试结果在 `build/test-results.json` 与 `build/ui-test-results.json`。这些本机构建产物不提交到源码仓库。

修改应围绕一个明确问题展开。迁移逻辑使用独立构造数据验证，避免操作真实存档；涉及已知缺陷时增加能复现该问题的回归测试。界面改动请检查不同窗口尺寸和系统缩放。

## 反馈与提交

- Issue 说明版本、环境、复现步骤及实际结果。
- PR 说明最终行为和验证结果；第三方代码或素材需说明来源与许可。
- 日志和截图隐去个人路径、账号及令牌，不提交真实游戏数据。

PR 会触发 `.github/workflows/ci.yml`，在 Windows 环境构建并执行测试。自动检查通过后仍需结合修改内容进行代码审查。
