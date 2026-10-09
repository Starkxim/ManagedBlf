# ManagedBlf

独立的 C# BLF 顺序读取库，附带一个用于展示和试用的 Windows GUI。解析库目标框架为 .NET 8 和 .NET 10，无第三方包和原生 DLL 依赖；GUI 使用 Windows Forms。

## 目录与运行

解析库位于 `src/ManagedBlf`，GUI 位于 `samples/ManagedBlf.Viewer`。保留在同一仓库，GUI 通过项目引用使用库，库不依赖 GUI。

```powershell
dotnet build ManagedBlf.sln -c Release
dotnet run --project samples/ManagedBlf.Viewer -c Release
```

构建使用 .NET 10 SDK；运行 GUI 需要 Windows 和 .NET 10 Desktop Runtime。核心保留 net8.0 消费兼容并增加 net10.0，运行使用对应运行时；已有 .NET 8 应用仍可引用 net8.0 核心，API 未改变。解析库可单独构建：

```powershell
dotnet build src/ManagedBlf/ManagedBlf.csproj -c Release
```

点击 **Load demo** 可读取程序现场生成的七条 CAN、CAN FD、LIN、文本对象；其中一条跨越两个容器，容器分别使用 zlib 和未压缩数据。**Save demo** 可保存合成文件，不包含真实业务日志。

GUI 在后台扫描整份文件，仅保留前 10,000 条预览。筛选只作用于预览；选中一行显示文本、解码提示和前 256 字节原始对象。取消和关闭在对象之间生效，进行中的对象读取或解压不能即时打断。

## Viewer alpha 打包

[下载/包说明](docs/VIEWER-DOWNLOADS.md) 区分 Windows x64 依赖框架与自包含包。`scripts/package-viewer.ps1` 构建并检查两个包；CI 验证 demo 生成、禁止覆盖和窗口启动，不发布。独立人工/tag 工作流准备下载物，仅显式 alpha tag 发布 prerelease。[Windows 打包/启动 run 37874480695](https://github.com/Starkxim/ManagedBlf/actions/runs/37874480695) 已通过两个包、demo 生成/禁止覆盖与包内容检查。CI 保留预览 ZIP 供审阅；公开 Release 和 GUI 交互验收仍待完成。

## 自动化验证

```sh
dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release
```

运行双目标测试需同时安装 .NET 8 和 .NET 10 运行时。fixtures 为独立构造的合成字节，见 [来源说明](tests/ManagedBlf.Tests/README.md)。CI 在 Linux/Windows 分别构建并测试两个核心目标，Windows 单独构建 GUI，不代表 GUI 交互验收。

2026-10-09 核对的 [微软支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) 明确 .NET 8 于 2026-11-10 结束支持，.NET 10 LTS 支持至 2028-11-14。保留 net8.0 是消费兼容安排，不延长微软支持期；新消费者优先使用 net10.0。核心未增加第三方包或原生依赖。

## 接口与边界

优先使用 `using var reader = BlfReader.Open(path)` 与 `ReadNext`。正常 EOF 返回 `false`；损坏对象、未知压缩方式、解压长度不符抛出明确异常。时间戳统一为从测量起点计的纳秒；文件墙钟时间不包含时区信息。

支持 CAN 1/86、CAN FD 100/101、LIN 11/57、APP_TEXT 65 的展示解码，其他类型保留原始数据。含扩展属性的 CAN FD 101 暂时保持原始对象。对于 101 的实际 payload 短于 validDataBytes 的情况保留真实字节并提示，不补造数据。APP_TEXT 默认按 UTF-8 展示，可在库调用时传入其他编码。

`ManagedBinlog` 提供熟悉的句柄函数名，但使用安全的 `Span<byte>` 和独立模型，不能原封不动替换原生 DLL 的 ABI。库不会构造文本或变长对象的非托管指针。同一句柄的单次操作串行化，peek/read 组合仍应由一个消费者执行。

完整限制见 [格式说明](docs/FORMAT.md)，手工验收见 [验收用例](docs/MANUAL-CHECKS.md)，来源见 [来源说明](docs/PROVENANCE.md)。本项目没有捆绑商业应用的其他源代码、原生 DLL 或日志，也不宣称覆盖全部 BLF 对象格式。

## 支持矩阵

| 能力 | 状态 |
| --- | --- |
| LOGG/LOBJ 顺序读取、compression 0/2、跨容器对象、有界 Span/句柄 API | 已实现 |
| Header v1/v2 flag 1/2 时间；CAN 1/86、FD 100/101、LIN 11/57、APP_TEXT 65 展示 | 已实现，限已说明布局 |
| 未知事件/错误、header v3、FD101 扩展属性 | 只保留 raw；未来 padding 不保证 |
| writer、索引/seek、更广的 typed 事件、NuGet 与 GUI 下载 | 计划实现 |

## 开发路线 / To-do

勾选代表已完成的对应范围；计划中的验收须实际执行后才勾选。边界见 [格式说明](docs/FORMAT.md)，人工操作见 [验收用例](docs/MANUAL-CHECKS.md)。

0. [x] 发布中英文有序路线与支持矩阵。验收：公开内容一致，保留使用和许可说明。
1. [x] 独立回归测试与 CI。从格式字段独立构造合成 fixtures，不复用 reader/demo 逻辑，说明可公开来源。覆盖 EOF/异常、签名/截断、compression 0/2/跨容器、zlib 头/校验/长度、资源上限、v1/v2 时间单位/溢出、raw、Span、peek/skip/句柄生命周期、七种 decoder 及 FD101 短数据/扩展变体。验收：Linux、Windows 的真实 Release 构建/测试 Actions 成功并记录测试数，Windows 单独构建 GUI。按微软最新支持政策评估 .NET 10 LTS，解释 .NET 8 消费兼容取舍；GUI 交互另做人工验收。
2. [ ] Windows viewer v0.1.0-alpha 下载。附 LICENSE、使用说明和现场生成的合成 demo，区分依赖框架与自包含包及运行时要求。发布采用独立人工/tag 工作流，仅发布 job 有写权限。验收：检查包内容、打包和启动；明确标注尚未完成的 Windows 交互验收。
3. [ ] 最小安全 writer，仅新建文件：可写/可寻址 stream、LOGG、LOBJ v1 纳秒时间、未压缩容器、CAN 1。说明所有权/leaveOpen、完成/重复完成/Dispose/失败行为和按格式统计的大小、计数、起止元数据；验证显式标准/扩展 ID 标志、RTR、DLC/payload、channel、时间，不截断或补造数据。验收：空文件、多对象、多容器、非法输入回归，typed 自互读、独立字节期望和独立 BLF 工具检查。首版不做 append、恢复、native 指针 ABI。
4. [ ] 逐项扩展 writer：zlib、CAN FD 100/101、LIN 11/57、APP_TEXT 65。每项验收须有边界回归、格式证据、矩阵和示例。FD101 扩展属性/header v3 在证据与测试齐备前继续 raw。将原生能力映射为安全 managed API；事件/错误解码、索引与性能按公开样本及需求排序，不承诺全面 BLF 覆盖。
5. [ ] 稳定 API、文档与打包。公开 API 说明所有权、异常、线程、生命周期，保持核心不依赖 GUI。验收：中英文 README、格式说明、手工验收同步，本地 NuGet pack/消费验证并附定制 LICENSE。公开 NuGet 发布及第三方贡献商业再许可政策须另行审阅决策，不假定拥有贡献者的商业再许可权。

2026-10-09 Linux 正式回归：net8.0、net10.0 各 58 项通过，零失败/跳过。[Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520) 的 Linux/Windows × net8.0/net10.0 各 58 项测试及独立 Windows viewer Release 构建已通过；GUI 交互仍待验收。

验收基线：独立回归与上述真实 Actions 已通过。已有合成检查及 Windows 构建/publish 不代表 GUI 交互、macOS/Linux 功能回归或完整外部互操作通过。

## 许可证

ManagedBlf 采用 [ManagedBlf 非商业源码公开许可 1.0](LICENSE)。版权所有 © 2026 Cao (Starkxim)。

- 非商业用途可免费使用，须保留版权和许可声明。
- 私人非商业自用不要求公开修改。向他人分发程序或提供使用本库的在线服务时，须公开对应版本的解析库及其全部修改源码；不要求公开整个应用。
- 商业用途必须另行购买书面许可，包括企业内部工具、受委托工作和商业研发，即使应用不出售、不对外分发。

商业授权请联系 [Cao (Starkxim)](https://github.com/Starkxim)。源码公开不代表免费商用。本定制许可不属于 OSI 批准的标准开源许可；完整条款以 LICENSE 中的英文文本为准。
