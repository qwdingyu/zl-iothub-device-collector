# 发布说明（RELEASE.md）

## 发布前检查（必须逐项过）

1. **清雷**（docs/188 §5.3 定义）：全目录无客户名 / 内网 IP / 逆向工程出处表述；
   `docs/NNN` 编号保留（官方溯源标记，README 已声明）；
2. **编译**：`dotnet build examples/CollectorExamples.csproj -c Debug` → 0 errors / 0 warnings；
3. **运行**（可选，需 S7 仿真器）：`dotnet run --project examples/CollectorExamples.csproj` → M1–M5 闭环；
4. **地址核对**：`address/` 每份速查与 17 协议地址格式规范章节一致（新增协议先补速查再发布）。

## 发布流程（GitHub）

```bash
git tag v1.0.0              # 版本号随变更语义递增
git push origin v1.0.0      # 触发 Actions build
# 确认 build 通过后创建 GitHub Release（附 changelog）
```

## 版本对照（每版包含什么）

| 版本 | 内容 |
|------|------|
| （占位） | — |

## Changelog

- （占位）