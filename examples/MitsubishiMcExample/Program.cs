// =============================================================================
// ZL.IotHub 三菱 MC 采集示例 —— SKILL（zl-iothub-device-collector）第三个协议示例
//
// 本示例按 SKILL 五步编写（使用验证）：
//   第 1 步 连接：DeviceConfig（Protocol=melsec-mc 路由自研 MitsubishiMcDriver；Version 可覆盖机型）
//   第 2 步 标签：地址按 address/mc.md —— MC 地址天然符号化（D100/M100/X0…），
//                ⚠️ 位区/字区区分：M/X/Y/L/B/S/F/V 是位区；D/R/ZR/W/SW 是字区
//   第 3 步 读取：MultiDeviceOrchestrator + 地址合并批量读（与 S7/Modbus 示例同构）
//   第 4 步 消息：PlcNotificationHub.TagChanges（值变更 + 质量码）+ DataCollected（读取完成快照）
//   第 5 步 清理：StopAsync 优雅停止 + 取消订阅
//
// 前置：PlcSimulator.Cli 已启动（MC 端口 35000）或真实三菱 PLC 可达：
//   dotnet run --project <sim>/src/PlcSimulator.Cli -- --protocols mc \
//       --ports mc=35000 --headless --stay-alive --auto-start
// 运行：dotnet run --project examples/MitsubishiMcExample
//   （--ip/--port 可覆盖；默认 127.0.0.1:35000）
//
// 授权：1 台 < 免费版 10 台上限，无需授权配置（docs/193）。
// =============================================================================

using ZL.IotHub;
using ZL.IotHub.Core;
using ZL.IotHub.Models;
using ZL.IotHub.Notifications;
using ZL.Tag;

namespace ZL.IotHub.MitsubishiMcExample;

internal static class Program
{
    private static readonly object Sync = new();

    private static async Task Main(string[] args)
    {
        // ── 连接参数（命令行可覆盖；默认连本机 PlcSimulator MC 35000）──
        var ip = GetArg(args, "--ip", "127.0.0.1");
        var port = int.TryParse(GetArg(args, "--port", "35000"), out var p) ? p : 35000;

        // ── 第 2 步 · 标签表（地址对照 address/mc.md）──
        // ⚠️ 位区（M/X/Y/L/B/S/F/V…）按位寻址；字区（D/R/ZR/W/SW…）按字寻址。
        // ⚠️ 批量位区已知缺口（docs/213 遗留#3）：采集引擎按"字块"读位区，MC 位区
        //    标签批量不出值（M96/M97 实测）→ 位区请走驱动直读 ReadBool；批量用 D 等字区。
        // ⚠️ X/Y/DX/DY 以 0 开头按 8 进制（X010=8 ≠ X10=16）；B/W 区是 16 进制。
        var tags = new List<TagItem>
        {
            new() { Id = "D_COUNT",  Address = "D100",  DataTypeCode = "short", TagType = "M" },   // 数据寄存器（字）
            new() { Id = "D_TEMP",   Address = "D102",  DataTypeCode = "short", TagType = "M" },   // 数据寄存器（字）
        };

        // ── 第 1 步 · 连接初始化（Protocol 路由自研 MitsubishiMcDriver；Version 默认 Qna_3E）──
        var cfg = new DeviceConfig();
        cfg.Add("Protocol", "melsec-mc");
        cfg.Add("DeviceId", "DEMO-MC");
        cfg.Add("DeviceIp", ip);
        cfg.Add("Port", port);
        cfg.Add("ReadInterval", 500);
        cfg.Add("Tags", tags);

        // ── 第 3 步 · 数据读取：编排器 + 地址合并批量读（与 S7/Modbus 示例同构）──
        using var orchestrator = new MultiDeviceOrchestrator(new List<DeviceConfig> { cfg });
        Console.WriteLine($"编排器已创建，设备数: {orchestrator.DeviceCount}");

        var device = DeviceManager.GetDeviceRootById("DEMO-MC")
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

        // ── 第 5 步 · 写值（typed 扩展；address=PLC 地址 "D100"，非标签 Id）──
        Console.WriteLine("示例：typed 写值 D_COUNT (D100) ← 1234（验证 SKILL M5 指引）...");
        var plc = device.Device ?? throw new InvalidOperationException("驱动未就绪");
        var writeResult = await plc.WriteAsync("D100", (short)1234);
        Console.WriteLine($"写值结果: IsSuccess={writeResult.IsSuccess}（{(writeResult.IsSuccess ? "ok" : writeResult.Message)}）");

        // 再采 3 秒，观察 D_COUNT 是否变为 1234（写生效 + 事件联动）
        await Task.Delay(TimeSpan.FromSeconds(3));

        // 优雅停止 + 资源清理（SKILL M5：先停数据流 → 再释放订阅/连接）
        await orchestrator.StopAsync(CancellationToken.None);
        PlcNotificationHub.Instance.TagChanges -= OnTagChanged;
        device.DataCollected -= OnDataCollected;
        connSub.Dispose();
        Console.WriteLine("已停止。Mitsubishi MC 示例运行完成 ✅");
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
                Console.WriteLine($"    {kv.Key,-8}= {kv.Value}  ({kv.Value?.GetType().Name})");
        }
    }

    private static string GetArg(string[] args, string key, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == key) return args[i + 1];
        return fallback;
    }
}