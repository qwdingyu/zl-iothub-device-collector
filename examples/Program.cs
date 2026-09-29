// =============================================================================
// ZL.IotHub 设备采集最小示例 —— SKILL「zl-iothub-device-collector」M1+M3 验证
//
// 演示能力（对应 SKILL 第 1 步 连接/初始化 + 第 3 步 数据读取）：
//   1. DeviceConfig 构建设备（Protocol=siemens-s7 路由到 S7 驱动，Tags 标签表）
//   2. MultiDeviceOrchestrator 起停（全局连接限流，防启动风暴）
//   3. device.DataCollected 事件拿「读取完成快照」（值稳定也会触发，见 demos/FleetS7 注释）
//
// 前置：S7Simulator.Standalone 已启动（--config plcs-e2e.json，端口 2002 起）
// 运行：dotnet run --project skills/zl-iothub-device-collector/examples
//
// 授权：1 台 < 免费版 10 台上限，无需授权配置（docs/193）。
//   ⚠️ 禁止把 demos/ 里的 EvaluationMode 授权路径复制进本示例（docs/196 门禁）。
// =============================================================================

using ZL.IotHub;
using ZL.IotHub.Core;
using ZL.IotHub.Models;
using ZL.IotHub.Notifications;
using ZL.Tag;

namespace ZL.IotHub.CollectorExamples;

internal static class Program
{
    // ── 连接参数（SKILL M1 参考值）──
    private const string SimulatorHost = "127.0.0.1";
    private const int Port = 2002;            // plcs-e2e.json 第一台 S7_1200 端口
    private const int ConnectTimeout = 8000;  // 连接超时（ms）
    private const int ReceiveTimeout = 5000;  // 接收超时（ms）
    private const int ReadInterval = 300;     // 采集周期（ms）
    private const int MaxGap = 50;            // 地址合并：允许的最大地址间隙（字节）
    private const int MaxBlockSize = 220;     // 地址合并：单块最大字节数（PDU 约束）

    private static readonly object Sync = new();

    private static async Task Main()
    {
        // ── 第 1 步 · 连接与初始化：构建单台设备配置 ──
        var config = new DeviceConfig();
        config.Add("DeviceId", "PLC-01");
        config.Add("DeviceIp", SimulatorHost);
        config.Add("Port", Port);
        config.Add("Rack", 0);
        config.Add("Slot", 1);
        config.Add("ReadInterval", ReadInterval);
        config.Add("MaxGap", MaxGap);
        config.Add("MaxBlockSize", MaxBlockSize);
        // ⚠️ 用 Protocol 键路由驱动（siemens-s7 → S7 驱动）；不要写 DeviceType——
        //    该键被 MultiDeviceOrchestrator 独占为"业务壳类全限定名"，与驱动型号是两个概念（demos/FleetS7 注释）。
        config.Add("Protocol", "siemens-s7");
        config.Add("ConnectTimeOut", ConnectTimeout);
        config.Add("ReceiveTimeOut", ReceiveTimeout);
        config.Add("Tags", BuildTags());      // 标签表（每个地址对照 address/s7.md 核对过）

        // ── 第 3 步 · 数据读取：编排器 + DataCollected 快照 ──
        using var orchestrator = new MultiDeviceOrchestrator(new List<DeviceConfig> { config });
        Console.WriteLine($"编排器已创建，设备数: {orchestrator.DeviceCount}");

        // 按 DeviceId 取设备实例并挂「读取完成」快照事件。
        // 注意：PlcNotificationHub.TagChanges 只在"值变化"时触发；统计/取数"读取完成"要用 DataCollected
        //（快照在每轮批量读取完成后触发，值稳定也会触发 —— 见 demos/FleetS7 注释）。
        var device = DeviceManager.GetDeviceRootById("PLC-01") ?? throw new InvalidOperationException("设备未找到");
        device.DataCollected += OnDataCollected;

        // 启动（编排器内部做全局连接限流，防 100 台同时 SYN 风暴；单台也走同一条路径）
        using var cts = new CancellationTokenSource();
        var startResults = await orchestrator.StartAsync(cts.Token);
        Console.WriteLine($"启动完成: 成功={startResults.Count(r => r.IsSuccess)}/{startResults.Count}");

        // ── 第 4 步 · 消息与质量：订阅「值变更」事件（含质量码）──
        // 注意：TagChanges 只在"值变化"时触发（与 DataCollected 的"读取完成"互补，见 FleetS7 注释）。
        // 高频场景改用 SubscribeChannel（Channel+背压管道，防快于慢 —— SKILL M4）；此处演示简单订阅。
        PlcNotificationHub.Instance.TagChanges += OnTagChanged;

        // 跑 5 秒：等价"每 ReadInterval(300ms) 一批批量读取"，观察 DataCollected 快照
        Console.WriteLine($"采集 5 秒（周期 {ReadInterval}ms）...");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // ── 第 5 步 · 写值（typed 扩展：address=PLC 地址、value=具体类型；禁裸字节）──
        // ⚠️ 注意：WriteAsync<T>(device, address, value) 的第一个参数是「PLC 地址」（如 "DB1.DBW2"=COUNT 标签），不是标签 Id。
        Console.WriteLine("示例：typed 写值 COUNT (DB1.DBW2) ← 1234（验证 SKILL M5 指引）...");
        // DeviceRoot 自身不实现 IPlcDevice；写操作走其持有的 IPlcDevice 字段（device.Device，连接后就绪）
        var plc = device.Device ?? throw new InvalidOperationException("驱动未就绪");
        var writeResult = await plc.WriteAsync("DB1.DBW2", (short)1234);
        Console.WriteLine($"写值结果: IsSuccess={writeResult.IsSuccess}（{(writeResult.IsSuccess ? "ok" : writeResult.Message)}）");

        // 再采 3 秒，观察 DataCollected/TagChanges 中 COUNT 是否变为 1234（写生效 + 事件联动）
        await Task.Delay(TimeSpan.FromSeconds(3));

        // 优雅停止 + 资源清理（SKILL M5：先停数据流 → 再释放订阅/连接）
        await orchestrator.StopAsync(CancellationToken.None);
        PlcNotificationHub.Instance.TagChanges -= OnTagChanged;
        device.DataCollected -= OnDataCollected;
        Console.WriteLine("已停止。示例运行完成 ✅");
    }

    /// <summary>
    /// 值变更事件回调（PlcNotificationHub.TagChanges）：打印"值变化"及其质量码。
    /// 质量码在事件层（SKILL M4）：Good=0xC0 正常、Uncertain=0x40 陈旧/初始化中、Bad 不可信 ——
    /// 业务侧消费前先看 Quality，不裸信值。
    /// </summary>
    private static void OnTagChanged(List<TagValueChange> changes)
    {
        lock (Sync)
        {
            foreach (var c in changes)
                Console.WriteLine($"    [变更] {c.TagId} = {c.Value}  Quality={c.Quality}");
        }
    }

    /// <summary>
    /// DataCollected 快照回调：打印本轮批量读取的所有标签值（TagId → 值）。
    /// 类型按标签 DataTypeCode 保真（bool/short/float/int/ushort），不是裸字节 —— SKILL M4 要求。
    /// </summary>
    private static void OnDataCollected(PlcCollectRecord record)
    {
        lock (Sync)
        {
            Console.WriteLine($"[{record.Timestamp:HH:mm:ss.fff}] {record.DeviceName} 快照：");
            foreach (var kv in record.Values.OrderBy(k => k.Key))
                Console.WriteLine($"    {kv.Key,-8}= {kv.Value}  ({kv.Value?.GetType().Name})");
        }
    }

    /// <summary>
    /// 标签点集（照 SKILL M2 地址核对 + demos/FleetS7 BuildTags）：DB1 内连续分配，
    /// 前 16 字节覆盖 bool/short/float/int/ushort，地址紧凑利于地址合并成一块（MaxGap 内）。
    /// 每个地址已对照 address/s7.md：DBX=位、DBW=字(2B)、DBD=双字(4B)。
    /// </summary>
    private static List<TagItem> BuildTags() => new()
    {
        new() { Id = "RUN",   Address = "DB1.DBX0.0",  DataTypeCode = "bool",   TagType = "M" },
        new() { Id = "FAULT", Address = "DB1.DBX0.1",  DataTypeCode = "bool",   TagType = "M" },
        new() { Id = "COUNT", Address = "DB1.DBW2",    DataTypeCode = "short",  TagType = "M" },
        new() { Id = "TEMP",  Address = "DB1.DBD4",    DataTypeCode = "float",  TagType = "M" },
        new() { Id = "PRESS", Address = "DB1.DBD8",    DataTypeCode = "float",  TagType = "M" },
        new() { Id = "SPEED", Address = "DB1.DBD12",   DataTypeCode = "int",    TagType = "M" },
        new() { Id = "LEVEL", Address = "DB1.DBD16",   DataTypeCode = "ushort", TagType = "M" },
    };
}