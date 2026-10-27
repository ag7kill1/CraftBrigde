# 验证记录

## 恢复边界补测 · 2026-10-27

基线为目录修复合并提交 [`a5253a1ae09bc5d6936d1415b864a23ddd15423e`](https://github.com/ag7kill1/CraftBrigde/commit/a5253a1ae09bc5d6936d1415b864a23ddd15423e)。[Windows CI #3](https://github.com/ag7kill1/CraftBrigde/actions/runs/40190394209) 验证该基线通过 68 项核心测试与 5 项界面测试；本轮补测结果来自本地 Windows 构建，不代表新提交的远程 CI。

本轮在该基线上执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`，并为跨卷测试指定第二卷的独立临时目录：**73 项核心测试 + 5 项界面测试通过，0 失败、0 跳过**。C 盘放置游戏目录，D 盘放置运行目录；通过 Windows 卷标识确认是两个不同的卷。测试结束后专属夹具目录已清理，没有访问真实游戏数据。

- 连续两次替换同一文件：两个任务目录分别保留原版和第一次导入的内容；恢复记录准确对应原位置及备份。
- 跨卷替换成功和后续取消：运行文件旁创建同卷备份，记录映射正确；取消时两个卷均还原，成功回退消耗对应备份；收到的 ZIP 哈希不变。
- 普通 `IOException`：确认 A 已新增、B 已替换，再在 C 提交复制时通过进度回调注入异常；A 撤回、B 还原，新建目录清理，既有空目录保留。
- 世界暂存取消：确认已暂存 `level.dat` 并开始暂存区块，再取消；本次任务目录整体消失。临时清理断言现检查所有任务子项，包括世界文件和空目录。
- 恢复记录：核对最后一条状态、原始原因及问题路径，覆盖成功、已回退和需手动恢复，包括目标被编辑、世界被编辑及备份缺失或篡改。
- 发行文档：随包提供 `docs/` 和 `CONTRIBUTING.md`，构建测试检查 Markdown 本地文件链接；本轮发行目录 14 个本地链接目标全部可解析。检查器的缺失链接拒绝行为使用独立夹具验证。

跨卷测试需显式设置已有且可写的第二卷目录，例如先创建专用 `D:\CraftBridge-TestParent`，然后运行：

```powershell
$env:CRAFTBRIDGE_TEST_SECONDARY_ROOT = 'D:\CraftBridge-TestParent'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
Remove-Item Env:\CRAFTBRIDGE_TEST_SECONDARY_ROOT
```

测试只在指定父目录中新建随机 `cross-volume-<GUID>` 子目录，并清理自己的子目录；指定目录与游戏夹具所在卷相同、卷无法确认或不可写时会失败。未设置此变量时，两个跨卷用例明确标为 **SKIP**，计入 `build/test-results.json` 的 `skipped`，不算通过；普通构建测试为 71 项核心通过、2 项跳过，加 5 项界面通过。默认 CI 不承诺第二卷可用。

本轮未修改迁移算法。尚未验证真实 Minecraft 启动、联机、大规模数据吞吐、网络共享及断电/强制结束后的恢复；进度回调注入的异常不等同于模拟磁盘硬件故障。断电或强制结束后的自动恢复尚未实现。

## CraftBridge v0.2.0 · 2026-10-25

Windows PowerShell 构建成功，**68 项核心测试 + 5 项界面状态测试全部通过**。新增回归验证 CraftBridge 的导出标识、旧 Blockporter 包读取，以及新旧工具工作目录的排除保护。

使用 computer-use 技能检查实际窗口：

- 导出页与导入页的新浅色布局、圆角控件、文件树和空状态。
- 左上角 CraftBridge 品牌缩为 15 pt，固定显示区域并留白，避免文字裁切。
- 在本机 150% 系统缩放下检查完整布局，修复缩放期间固定行高被反复放大、输入行挤压按钮文字的问题。
- 读取 11 文件 / 约 1.2 MB 的独立构造迁移包，预览新增 11，再从界面完成导入。
- 文件系统确认测试世界和模组已导入、收到的 ZIP 保留、`.craftbridge/tasks/` 下暂存文件为 0。

界面验收使用 `build/preview-fixture-*` 构造数据，不具备可游玩的世界和模组，未修改真实游戏。最后仅调整帮助页兼容说明，再编译发行程序。

## v0.1.1 · 2026-10-25

使用 Windows PowerShell 执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test`，发行程序编译成功。**66 项核心测试、5 项界面状态测试通过，0 失败**。

- `tests/EngineTests.cs`：数据往返、分类选择、复用与备份替换、原 ZIP 保留、世界整体提交和回退、目录竞争、改名冲突、取消与临时清理异常、运行目录映射、路径和归档校验。
- `tests/CoreAuditTests.cs`：运行文件伪装存档、实际映射越界、接收 ZIP 路径冲突、备份被篡改，以及暂存目录中的未声明文件和空目录。
- `tests/DiscoveryTests.cs`：共享与隔离目录、Prism、继承关系、缺失依赖、扫描排除项和取消。
- `tests/UiTests.cs`：在 STA 线程创建内部控件，不显示窗口；验证切换源与目标使旧状态失效、手动添加保留选择、3000 文件的树统计。

结果输出为 `build/test-results.json` 和 `build/ui-test-results.json`。测试使用 `build/` 下的独立构造数据，不访问或改动真实游戏，构造的世界和模组不可游玩。本轮没有新增人工界面点击验收；以下 v0.1.0 界面记录仅代表当时版本。

完整问题与修复说明见 [v0.1.1 审查记录](REVIEW-v0.1.1.md)。尚未验证真实游戏启动与联机、大规模实机吞吐、网络共享和断电恢复。

## v0.1.0 · 2026-10-24

原版核心测试：**41 通过、0 失败**。使用 computer-use 技能实际检查：

1. 扫描并默认选择有数据的 Fabric 测试实例，切换用途预设和全选。
2. 打开原生 ZIP 保存窗口；导出内容由核心往返测试验证。
3. 在界面读取含 5 个文件的测试 ZIP，选择独立 B 机目录，预览显示新增 5、写入 121 B。
4. 界面导入成功；确认模组和世界到达目标、B 机额外模组与原 ZIP 保留、临时任务内容为 0。
5. 再次预览同一 ZIP，显示复用 5、写入 0 B。
6. 修复文字裁切、长路径撑宽布局和默认选择空共享实例的问题。

所有操作均为构造数据，没有启动真实 Minecraft 或验证联机。
