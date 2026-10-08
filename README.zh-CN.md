# ManagedBlf

独立的 C# BLF 顺序读取库，附带一个用于展示和试用的 Windows GUI。解析库目标框架为 .NET 8，无第三方包和原生 DLL 依赖；GUI 使用 Windows Forms。

## 目录与运行

解析库位于 `src/ManagedBlf`，GUI 位于 `samples/ManagedBlf.Viewer`。保留在同一仓库，GUI 通过项目引用使用库，库不依赖 GUI。

```powershell
dotnet build ManagedBlf.sln -c Release
dotnet run --project samples/ManagedBlf.Viewer -c Release
```

运行 GUI 需要 Windows 和 .NET 8 Desktop Runtime。解析库可单独构建：

```powershell
dotnet build src/ManagedBlf/ManagedBlf.csproj -c Release
```

点击 **Load demo** 可读取程序现场生成的七条 CAN、CAN FD、LIN、文本对象；其中一条跨越两个容器，容器分别使用 zlib 和未压缩数据。**Save demo** 可保存合成文件，不包含真实业务日志。

GUI 在后台扫描整份文件，仅保留前 10,000 条预览。筛选只作用于预览；选中一行显示文本、解码提示和前 256 字节原始对象。取消和关闭在对象之间生效，进行中的对象读取或解压不能即时打断。

## 接口与边界

优先使用 `using var reader = BlfReader.Open(path)` 与 `ReadNext`。正常 EOF 返回 `false`；损坏对象、未知压缩方式、解压长度不符抛出明确异常。时间戳统一为从测量起点计的纳秒；文件墙钟时间不包含时区信息。

支持 CAN 1/86、CAN FD 100/101、LIN 11/57、APP_TEXT 65 的展示解码，其他类型保留原始数据。含扩展属性的 CAN FD 101 暂时保持原始对象。对于 101 的实际 payload 短于 validDataBytes 的情况保留真实字节并提示，不补造数据。APP_TEXT 默认按 UTF-8 展示，可在库调用时传入其他编码。

`ManagedBinlog` 提供熟悉的句柄函数名，但使用安全的 `Span<byte>` 和独立模型，不能原封不动替换原生 DLL 的 ABI。库不会构造文本或变长对象的非托管指针。同一句柄的单次操作串行化，peek/read 组合仍应由一个消费者执行。

完整限制见 [格式说明](docs/FORMAT.md)，手工验收见 [验收用例](docs/MANUAL-CHECKS.md)，来源见 [来源说明](docs/PROVENANCE.md)。本项目没有捆绑商业应用的其他源代码、原生 DLL 或日志，也不宣称覆盖全部 BLF 对象格式。
