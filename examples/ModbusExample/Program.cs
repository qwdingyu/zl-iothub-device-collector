// =============================================================================
// ZL.IotHub Modbus 采集示例 —— SKILL（zl-iothub-device-collector）第二个协议示例
//
// 本示例按 SKILL 五步编写（使用验证）：
//   第 1 步 连接：DeviceConfig（Protocol=modbus-tcp + Modbus 专属键 Station/AddressStartWithZero）
//   第 2 步 标签：地址按 address/modbus.md —— ⚠️ 自研驱动（PlcAddressHelper）支持 COIL/HR/DI 文本前缀；
//                HSL 客户端路径（HslDriverFactory/ModbusTcpNet）不认文本前缀（两套地址语义，见 address/modbus.md）
//   第 3 步 读取：MultiDeviceOrchestrator + 地址合并批量读（与 S7 示例同构；Modbus 无字/字节之分需按寄存器连续合并）
//   第 4 步 消息：PlcNotificationHub.TagChanges（值变更 + 质量码）+ DataCollected（读取完成快照）
//   第 5 步 清理：StopAsync 优雅停止 + 取消订阅
//
// 前置：PlcSimulator.Cli 已启动（modbus 端口 31502）或真实 Modbus 设备可达：
//   dotnet run --project <sim>/src/PlcSimulator.Cli -- --protocols modbus-tcp \
//       --ports modbus=31502 --headless --auto-start
// 运行：dotnet run --project examples/ModbusExample
//   （--ip/--port/--station 可覆盖；默认 127.0.0.1:31502 station=1 zeroBased）
//
// 授权：1 台 < 免费版 10 台上限，无需授权配置（docs/193）。
// =============================================================================

using ZL.IotHub;
using ZL.IotHub.Core;
using ZL.IotHub.Models;
using ZL.IotHub.Notifications;
using ZL.Tag;

namespace ZL.IotHub.ModbusExample;

internal static class Program
{
    private static readonly object Sync = new();

    private static async Task Main(string[] args)
    {
        // ── 连接参数（命令行可覆盖；默认连本机 PlcSimulator modbus 31502）──
        var ip = GetArg(args, "--ip", "127.0.0.1");
        var port = int.TryParse(GetArg(args, "--port", "31502"), out var p) ? p : 31502;
        var station = byte.TryParse(GetArg(args, "--station", "1"), out var st) ? st : (byte)1;
        var zeroBased = !args.Contains("--one-based");

        // ── 第 2 步 · 标签表（地址对照 address/modbus.md —— 三栈差异）──
        // ⚠️ 本示例走「自研 ModbusTcpDriver」：地址 = **纯数字寄存器索引（0-based）**，
        //    如 "0"/"100"（StandardModbusAddressMapper；ModbusCrossTest 交叉测试全绿实证）。
        //    ❌ 文本前缀 COIL1/HR100 在通用 Modbus 驱动路径「地址解析失败」（实测）——
        //       文本前缀仅品牌子类 AddressMapper（汇川/信捷等）支持；
        //    ❌ HSL 客户端路径（HslDriverFactory）同样不认文本前缀，只认纯数字/富地址（s=;x=;addr）。
        //    （两套语义详见 address/modbus.md「三栈差异」）
        var tags = new List<TagItem>
        {
            // 线圈区（FC01 读 / FC05 写）：COIL1→寄存器 0、COIL2→寄存器 1
            new() { Id = "COIL_RUN",   Address = "0",    DataTypeCode = "bool",  TagType = "M" },
            new() { Id = "COIL_FAULT", Address = "1",    DataTypeCode = "bool",  TagType = "M" },
            // 保持寄存器区（FC03 读 / FC06 写）：HR100→寄存器 100
            new() { Id = "HR_COUNT",   Address = "100",  DataTypeCode = "short", TagType = "M" },
            // ⚠️ 模拟器只对「已初始化/已写入」的寄存器有响应（未预置区读返回异常，该标签不出值）；
            //    真实 PLC 无此限制 —— 若加新标签（如 101）发现无值，先写一次即可激活。
        };

        // ── 第 1 步 · 连接初始化（Protocol 路由自研 ModbusTcpDriver；Station 为 Modbus 专属键）──
        // AddressStartWithZero：品牌子类（汇川等）专属键，通用 ModbusTcpDriver 直接按 0-based 寄存器索引，保留无害。
        var cfg = new DeviceConfig();
        cfg.Add("Protocol", "modbus-tcp");
        cfg.Add("DeviceId", "DEMO-MODBUS");
        cfg.Add("DeviceIp", ip);
        cfg.Add("Port", port);
        cfg.Add("Station", station);
        cfg.Add("AddressStartWithZero", zeroBased);
        cfg.Add("ReadInterval", 500);
        cfg.Add("Tags", tags);

        // ── 第 3 步 · 数据读取：编排器 + 地址合并批量读（与 S7 示例同构）──
        using var orchestrator = new MultiDeviceOrchestrator(new List<DeviceConfig> { cfg });
        Console.WriteLine($"编排器已创建，设备数: {orchestrator.DeviceCount}");

        var device = DeviceManager.GetDeviceRootById("DEMO-MODBUS")
                    ?? throw new InvalidOperationException("设备未找到");
        device.DataCollected += OnDataCollected;   // 「读取完成」快照（值稳定也触发）

        // ── 第 4 步 · 消息与质量 ──
        // 连接状态（诊断用）：Kind=Connection → PingOk/Connected
        using var connSub = PlcNotificationHub.Instance.Subscribe(evt =>
        {
            if (evt.Kind != PlcRealtimeEventKind.Connection) return;
            Console.WriteLine($"    [连接] Device={evt.DeviceId} PingOk={evt.PingOk} Connected={evt.Connected}");
        });
        // 值变更（含质量码）：Good=0xC0 正常 / Uncertain=0x40 陈旧 / Bad 不可信 —— SKILL M4
        PlcNotificationHub.Instance.TagChanges += OnTagChanged;

        using var cts = new CancellationTokenSource();
        var startResults = await orchestrator.StartAsync(cts.Token);
        Console.WriteLine($"启动完成: 成功={startResults.Count(r => r.IsSuccess)}/{startResults.Count}");

        // 采 5 秒观察 DataCollected 快照 + 连接状态
        Console.WriteLine($"采集 5 秒（周期 500ms）...");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // ── 第 5 步 · 写值（typed 扩展；address=PLC 地址 "100"=HR100，非标签 Id）──
        Console.WriteLine("示例：typed 写值 HR_COUNT (寄存器 100) ← 1234（验证 SKILL M5 指引）...");
        var plc = device.Device ?? throw new InvalidOperationException("驱动未就绪");
        var writeResult = await plc.WriteAsync("100", (short)1234);
        Console.WriteLine($"写值结果: IsSuccess={writeResult.IsSuccess}（{(writeResult.IsSuccess ? "ok" : writeResult.Message)}）");

        // 再采 3 秒，观察 HR_COUNT 是否变为 1234（写生效 + 事件联动）
        await Task.Delay(TimeSpan.FromSeconds(3));

        // 优雅停止 + 资源清理（SKILL M5：先停数据流 → 再释放订阅/连接）
        await orchestrator.StopAsync(CancellationToken.None);
        PlcNotificationHub.Instance.TagChanges -= OnTagChanged;
        device.DataCollected -= OnDataCollected;
        connSub.Dispose();
        Console.WriteLine("已停止。Modbus 示例运行完成 ✅");
    }

    /// <summary>值变更回调（PlcNotificationHub.TagChanges）：打印变化值 + 质量码。</summary>
    private static void OnTagChanged(List<TagValueChange> changes)
    {
        lock (Sync)
        {
            foreach (var c in changes)
                Console.WriteLine($"    [变更] {c.TagId} = {c.Value}  Quality={c.Quality}");
        }
    }

    /// <summary>DataCollected 快照回调：打印本轮批量读取的所有标签值（类型保真，非裸字节 —— SKILL M4）。</summary>
    private static void OnDataCollected(PlcCollectRecord record)
    {
        lock (Sync)
        {
            Console.WriteLine($"[{record.Timestamp:HH:mm:ss.fff}] {record.DeviceName} 快照：");
            foreach (var kv in record.Values.OrderBy(k => k.Key))
                Console.WriteLine($"    {kv.Key,-10}= {kv.Value}  ({kv.Value?.GetType().Name})");
        }
    }

    private static string GetArg(string[] args, string key, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == key) return args[i + 1];
        return fallback;
    }
}