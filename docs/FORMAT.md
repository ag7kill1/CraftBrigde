# CraftBridge ZIP v1

本文件描述首版公开格式，方便人工恢复和后续独立实现。

CraftBridge 自 v0.2.0 使用新应用名称，`FormatVersion` 保持 `1`。读取时同时接受 `Application` 为 `CraftBridge` 或旧值 `Blockporter`；新导出统一写入 `CraftBridge`，不会改写收到的旧 ZIP。旧版 Blockporter 程序不识别新应用标识，接收新版迁移包时请使用 CraftBridge。

```
example.zip
├─ manifest.json
└─ payload/
   ├─ game/       # 相对所选游戏目录的文件
   └─ runtime/    # 可选，相对标准 .minecraft 运行根目录的文件
```

ZIP 内没有启动器账号、机器绝对路径和目录联接。`game/` 可包含存档、模组、配置、资源、个人设置、截图和手动加入的其他目录。`runtime/` 仅允许 `versions/`、`libraries/`、`assets/`。ZIP 中不保存空目录和 `session.lock`。

从程序 v0.1.1 起，`runtime/versions/` 仅接受 `versions/<版本>/<版本>.json` 和 `versions/<版本>/<版本>.jar`，不可夹带隔离实例的 `saves/` 等游戏数据。这与 v0.1.0 的标准运行文件导出结果兼容；格式编号仍为 `1`。运行文件映射后的实际位置也不得进入目标实例的游戏数据目录或迁移工具内部目录。

`manifest.json` 是 UTF-8 JSON，字段使用以下大小写：

- `FormatVersion`：整数 `1`。
- `Application`：`CraftBridge`（兼容读取旧值 `Blockporter`）。
- `Edition`：`java`。
- `CreatedUtc`：ISO 8601 UTC 时间。
- `Name`、`Purpose`：实例名称与导出用途。
- `MinecraftVersion`、`VersionId`、`Loader`、`LoaderVersion`：从本地元数据识别的信息；可能为空，不能据此保证兼容。
- `RuntimeIncluded`、`RuntimeComplete`：是否包含运行文件、该次选择是否完整覆盖已识别依赖。后者不包括 Java 和额外安装脚本的执行，也不代替游戏启动检查。
- `Notes`：扫描发现的限制及缺失项。
- `Files`：每项含 `Path`、`Category`、`Size`、`Sha256`、`ModifiedUtc`。

`Path` 使用 `/`，例如 `game/mods/example.jar`；ZIP 中对应 `payload/game/mods/example.jar`。`Size` 为原始字节数，`Sha256` 为 64 位小写十六进制内容摘要。时间为 ISO 8601。分类为 `worlds`、`mods`、`config`、`resources`、`maps`、`personal`、`screenshots`、`custom` 或 `runtime`，应与文件路径相符。

导入器验证路径、数量、解压大小、分类、重复及父子文件冲突；禁止 `..`、绝对路径、盘符、Windows 保留名称、工具内部路径、账号文件及 `session.lock`。工具内部路径同时包括 `.craftbridge*` 与历史 `.blockporter*` 前缀，不区分大小写。有效 ZIP 中每个文件都必须有清单记录；不能通过额外文件覆盖目标。需要写入的内容在修改目标前验证 SHA-256。

手动恢复时，可将 `payload/game/` 中的文件按原有相对路径取出。世界按整个世界文件夹恢复到目标 `saves/` 下；先在目标启动器准备相同的版本与加载器，保留已有数据的副本。
