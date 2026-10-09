# Windows viewer alpha / Windows 查看器 alpha

Early preview of the limited managed reader. Windows x64 only. GUI mouse
interaction remains unverified; a startup smoke check does not complete the
[manual acceptance checklist](MANUAL-CHECKS.md). No native binlog.dll or real logs
are included. The bundled LICENSE governs use; commercial use needs separate
written permission. Corresponding source is the exact release tag in
https://github.com/Starkxim/ManagedBlf .

有限解析能力的早期预览，仅 Windows x64。尚未完成 GUI 鼠标交互验收，启动检查
不代表手工用例全部通过。包不含原生 binlog.dll 或真实日志。使用须遵守附带
LICENSE；商用需另行书面许可。对应源码见上述仓库的同名 release tag。

- **framework-dependent**: install the current .NET 10 Desktop Runtime x64,
  extract the whole ZIP, then run `ManagedBlf.Viewer.exe`.
- **self-contained**: runtime included; extract the whole ZIP and run the EXE.
  Runtime security updates require a refreshed package.
- **依赖框架包**：安装 .NET 10 Desktop Runtime x64，完整解压 ZIP 后启动 EXE。
- **自包含包**：包含运行时，完整解压后启动；更新运行时需更新整个包。

Both packages include apphost license/third-party notices in `third-party-licenses`;
the self-contained package also includes the licenses/notices from its actual
.NET and Windows Desktop runtime packs. These components retain their own terms;
the ManagedBlf LICENSE is unchanged.

两个包都在 `third-party-licenses` 附上 apphost 的许可/第三方通知；自包含包另附
实际 .NET 与 Windows Desktop 运行时包的许可/通知。这些组件适用各自条款，
ManagedBlf 的 LICENSE 保持不变。

Use **Load demo** to generate seven synthetic objects in memory; **Save demo**
saves the fixture interactively. For a new file from the command line:

```powershell
./ManagedBlf.Viewer.exe --save-demo ./demo.blf
```

The command never overwrites an existing file. 通过 Load demo 现场生成七种合成对象；
Save demo 可交互保存。命令行生成只接受新文件，不覆盖已有日志。

The preview retains the first 10,000 objects; filtering only searches this preview.
Cancellation takes effect between objects. Verify ZIPs against SHA256SUMS.txt.
预览只保留前 10,000 对象，筛选只作用于预览；取消在对象之间生效。
下载后可通过 SHA256SUMS.txt 校验 ZIP。

Maintainers: `scripts/package-viewer.ps1` publishes and inspects both variants,
checks new-file demo generation and creates a window for five seconds before
closing it. The separate release workflow runs regression first. Manual dispatch
creates artifacts only; an explicit alpha tag publishes a prerelease with write
permission confined to the release job. Build outputs are never source commits. A `v<alpha-version>-package.<revision>`
tag explicitly corrects packaging for the existing alpha release: application
source is checked out from the original immutable release tag, and only the
packaging script is taken from the correction tag. Assets/checksums and Release
notes are refreshed; the original release tag is never moved.

The bundled manual checklist is the original source-tag snapshot; its historical
release-pending wording does not describe current download availability. Use this
page for current publication status. 包内人工清单保留原源码 tag 的文档快照；其中
旧的待发布文字不代表当前下载状态，最新发布状态以本说明为准。

## Published v0.1.0-alpha / 已发布版本

[Release](https://github.com/Starkxim/ManagedBlf/releases/tag/v0.1.0-alpha) · [Exact source](https://github.com/Starkxim/ManagedBlf/tree/v0.1.0-alpha) ·
[Release workflow](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845)

| Variant / 包 | Bytes / 字节 | SHA-256 |
| --- | --- | --- |
| Framework-dependent / 依赖框架 | 153179 | `d992c212ec2d419d5e336a1b839ecc6c9cf40a0dcc620d2cc89cf4c4ede44d22` |
| Self-contained / 自包含 | 51549520 | `7cc81e0b5dc6680260beaa5d3a252925b74280d624cdac1f2b7b65270c044c73` |

Validated on 2026-10-09: actual release workflow, 58 Windows regression tests per
runtime, both package/demo/overwrite/window checks, and independent download
inspection of published ZIPs against SHA256SUMS.txt. The self-contained package
includes .NET 10.0.12. LICENSE text is unchanged (Windows CRLF line endings). The corrected ZIPs
include exact official 10.0.12 apphost/runtime license notices; assets were
refreshed by `v0.1.0-alpha-package.1`, without moving the original source tag.

2026-10-09 已验证真实发布流程、Windows 双运行时各 58 项回归、双包内容/demo/
禁止覆盖/窗口启动，以及实际下载 ZIP 的校验和。自包含包内含 .NET 10.0.12；
许可文本未变，Windows 包使用 CRLF 换行。修正包已附官方 10.0.12 apphost/运行时
许可通知；打包修正 tag 为 `v0.1.0-alpha-package.1`，原源码 tag 未移动。
GUI 鼠标交互仍待验收。
