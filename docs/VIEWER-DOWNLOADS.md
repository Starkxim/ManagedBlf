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
permission confined to the release job. Build outputs are never source commits.
