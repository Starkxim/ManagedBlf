# ManagedBlf

独立的 C# BLF 库，支持顺序读取和有限的 CAN 写入，附带一个用于展示和试用的 Windows GUI。解析库目标框架为 .NET 8 和 .NET 10，无第三方包和原生 DLL 依赖；GUI 使用 Windows Forms。

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

## Viewer alpha 下载

[v0.1.0-alpha Windows x64 预发布版](https://github.com/Starkxim/ManagedBlf/releases/tag/v0.1.0-alpha) 已提供两个 ZIP 和 [SHA256SUMS.txt](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/SHA256SUMS.txt)：

| 下载 | 运行时要求 | 大小 |
| --- | --- | --- |
| [依赖框架包](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/ManagedBlf.Viewer-0.1.0-alpha-win-x64-framework-dependent.zip) | 安装 .NET 10 Desktop Runtime x64 | 153,179 字节 |
| [自包含包](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/ManagedBlf.Viewer-0.1.0-alpha-win-x64-self-contained.zip) | 内含 .NET 10.0.12 与 Windows Desktop 运行时 | 51,549,520 字节 |

完整解压后运行 `ManagedBlf.Viewer.exe`。两个包均附 LICENSE、双语使用说明和手工验收清单。`third-party-licenses` 提供 apphost 通知；自包含 ZIP 另附实际运行时的许可/通知，适用各自条款。**Load demo** 在内存现场生成合成数据；`--save-demo` 只新建合成文件，不覆盖已有路径。[精确对应源码](https://github.com/Starkxim/ManagedBlf/tree/v0.1.0-alpha) 为提交 `8ec6a72`。这些现有 alpha 下载包包含 reader/viewer，不包含当前源码新增的 writer。

[发布工作流 run 37877253845](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845) 已通过 Windows net8.0/net10.0 各 58 项回归、双包内容检查、demo 生成/禁止覆盖及实际窗口启动。2026-10-09 已下载两个公开 ZIP，核对 ZIP 完整性、SHA-256、许可文本、运行时配置、排除内容，并将第三方通知逐字节对照官方 10.0.12 NuGet 包。包不含原生 `binlog.dll`、真实日志、商业应用源码、凭据或源码构建目录。Windows GUI 鼠标交互仍待验收；启动检查不代表人工验收完成。详见 [下载/包说明](docs/VIEWER-DOWNLOADS.md)。

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

## 新建 CAN 文件

```csharp
using ManagedBlf;

// SYSTEMTIME 不含时区：显式传入所需的墙钟时间。
var start = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Unspecified);
using var writer = BlfWriter.Create("new-capture.blf", start);
writer.WriteCanMessage(new BlfCanFrame
{
    TimestampNanoseconds = 1_000_000,
    Channel = 1,
    Identifier = 0x42,
    IsExtended = true, // ID 较小时也须显式指定扩展格式。
    IsTransmit = true,
    Dlc = 2,
    Data = new byte[] { 0x12, 0x34 }
});
writer.Complete();
```

`Create` 使用 `FileMode.CreateNew`，不覆盖已有路径。stream 构造函数只接受位于零位置的空、可写、可寻址流；`leaveOpen: true` 在 Dispose 后保留流。起点时间须为 `Unspecified`、毫秒对齐、年份 1601–9999。相对纳秒须非负，且加入起点后不能溢出墙钟范围。channel 为 1–65535，DLC 为 0–8，标准/扩展 ID 按 `IsExtended` 分别限制为 11/29 位。payload 长度须精确匹配 DLC；RTR（`IsRemote`）保留 DLC，但 payload 必须为空。不截断或补造调用者数据。

writer 在有界未压缩容器中缓冲完整 CAN 1 对象。`Complete` 刷出数据并回填大小、计数、时间；成功后在 Dispose 前重复调用无操作。`Dispose` 完成健康 writer 并关闭其拥有的流，完成抛异常时也关闭。参数错误可修正后重试；I/O 故障永久阻止后续写入/完成，Dispose 不重试最终化。失败可能留下部分文件；创建不具事务性，Flush 不保证数据已持久写入磁盘。实例限单消费者；成功完成前由 writer 独占使用流。完整说明见 [writer 格式/API](docs/FORMAT.md#minimal-writer)。

## 支持矩阵

| 能力 | 状态 |
| --- | --- |
| LOGG/LOBJ 顺序读取、compression 0/2、跨容器对象、有界 Span/句柄 API | 已实现 |
| Header v1/v2 flag 1/2 时间；CAN 1/86、FD 100/101、LIN 11/57、APP_TEXT 65 展示 | 已实现，限已说明布局 |
| 未知事件/错误、header v3、FD101 扩展属性 | 只保留 raw；未来 padding 不保证 |
| Windows x64 alpha viewer 下载 | 已发布；启动已检查，GUI 交互待验收 |
| 新建 CAN 1 writer；LOBJ v1 纳秒；未压缩容器 | 当前源码已实现并验收；现有 alpha 下载包不含 writer |
| writer 的 zlib、CAN FD、LIN、APP_TEXT；索引/seek、更广的 typed 事件、NuGet | 计划实现 |

## 开发路线 / To-do

勾选代表已完成的对应范围；计划中的验收须实际执行后才勾选。边界见 [格式说明](docs/FORMAT.md)，人工操作见 [验收用例](docs/MANUAL-CHECKS.md)。

0. [x] 发布中英文有序路线与支持矩阵。验收：公开内容一致，保留使用和许可说明。
1. [x] 独立回归测试与 CI。从格式字段独立构造合成 fixtures，不复用 reader/demo 逻辑，说明可公开来源。覆盖 EOF/异常、签名/截断、compression 0/2/跨容器、zlib 头/校验/长度、资源上限、v1/v2 时间单位/溢出、raw、Span、peek/skip/句柄生命周期、七种 decoder 及 FD101 短数据/扩展变体。验收：Linux、Windows 的真实 Release 构建/测试 Actions 成功并记录测试数，Windows 单独构建 GUI。按微软最新支持政策评估 .NET 10 LTS，解释 .NET 8 消费兼容取舍；GUI 交互另做人工验收。
2. [x] Windows viewer v0.1.0-alpha 下载。附 LICENSE、使用说明和现场生成的合成 demo，区分依赖框架与自包含包及运行时要求。发布采用独立人工/tag 工作流，仅发布 job 有写权限。验收：检查包内容、打包和启动；明确标注尚未完成的 Windows 交互验收。
3. [x] 最小安全 writer，仅新建文件：可写/可寻址 stream、LOGG、LOBJ v1 纳秒时间、未压缩容器、CAN 1。说明所有权/leaveOpen、完成/重复完成/Dispose/失败行为和按格式统计的大小、计数、起止元数据；验证显式标准/扩展 ID 标志、RTR、DLC/payload、channel、时间，不截断或补造数据。验收：空文件、多对象、多容器、非法输入回归，typed 自互读、独立字节期望和独立 BLF 工具检查。首版不做 append、恢复、native 指针 ABI。
4. [ ] 逐项扩展 writer：zlib、CAN FD 100/101、LIN 11/57、APP_TEXT 65。每项验收须有边界回归、格式证据、矩阵和示例。FD101 扩展属性/header v3 在证据与测试齐备前继续 raw。将原生能力映射为安全 managed API；事件/错误解码、索引与性能按公开样本及需求排序，不承诺全面 BLF 覆盖。
5. [ ] 稳定 API、文档与打包。公开 API 说明所有权、异常、线程、生命周期，保持核心不依赖 GUI。验收：中英文 README、格式说明、手工验收同步，本地 NuGet pack/消费验证并附定制 LICENSE。公开 NuGet 发布及第三方贡献商业再许可政策须另行审阅决策，不假定拥有贡献者的商业再许可权。

2026-10-09 Linux 正式回归：net8.0、net10.0 各 58 项通过，零失败/跳过。[Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520) 的 Linux/Windows × net8.0/net10.0 各 58 项测试及独立 Windows viewer Release 构建已通过；GUI 交互仍待验收。

第 3 阶段本地 Linux 回归已通过：每目标 121 项（原 58 项 reader/decoder + 新增 63 项 writer），零失败/跳过，构建零警告/错误。net8.0、net10.0 双向 python-can 4.6.1 检查均通过，两目标生成的 fixture 字节一致。[Actions run 37895947121](https://github.com/Starkxim/ManagedBlf/actions/runs/37895947121) 的 7 个 jobs 全部通过：Linux/Windows × net8.0/net10.0 核心回归（各 121 项）、两个 Linux 外部检查和 Windows viewer 构建/打包/启动检查。独立字面量字节期望与外部检查只覆盖最小 CAN writer。GUI 交互、macOS 回归和完整外部 BLF 互操作仍未验证。

## 许可证

ManagedBlf 采用 [ManagedBlf 非商业源码公开许可 1.0](LICENSE)。版权所有 © 2026 Cao (Starkxim)。

- 非商业用途可免费使用，须保留版权和许可声明。
- 私人非商业自用不要求公开修改。向他人分发程序或提供使用本库的在线服务时，须公开对应版本的解析库及其全部修改源码；不要求公开整个应用。
- 商业用途必须另行购买书面许可，包括企业内部工具、受委托工作和商业研发，即使应用不出售、不对外分发。

商业授权请联系 [Cao (Starkxim)](https://github.com/Starkxim)。源码公开不代表免费商用。本定制许可不属于 OSI 批准的标准开源许可；完整条款以 LICENSE 中的英文文本为准。
