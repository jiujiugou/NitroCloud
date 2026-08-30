# ADR-017：元数据只读约束（站点/设备/点位禁止手动新增/删除，由自动注册驱动）

- 日期：2026-08-30
- 模块：architecture（跨 Api / Persistence / 前端）

## 问题
- ADR-013 已实现「测量数据到达 → 自动注册站点/设备/点位」，但管理面板仍保留「新增/删除」入口（手动 CRUD 并存）。
- 用户拍板：站点不应手动新注册、设备/点位不应手动新增，否则又变成「把边缘网关的各种搬过来」，与自动注册重复/打架；写值（命令下发）是唯一应留给操作者的能力。

## 决策
- 元数据只读：站点/设备/点位由上行数据自动注册驱动（ADR-013），禁止手动新增/删除。
- 后端：新增配置 `Metadata:AllowManualCreate`（**默认 false**）。关闭时 `Sites/Devices/Points` 的 `POST`（新增）与 `DELETE`（删除）返回 `403 MetadataReadOnly`；`PUT`（改名/补全）保留。
- 命令写值（`POST /api/commands/write`）保留不变（ADR-015 登录 + 审计 `requested_by`）。
- 前端：SitesView / DevicesView / PointsView 去掉「新建/删除」按钮及相关调用，保留列表、编辑（改名/补全）；大屏写值入口不动。

## 载荷墙（改了会破坏什么）
- `Storage/` 接口只增不删：不动 `IMetadataStore`；自动注册链路（ADR-013）完全不变。
- 不动网关契约（上行/下行仍以 NitroGateway 为准）。
- 写值链路（Command/回执）不动；大屏匿名只读端点不动。
- 用「开关 + 403」而非删端点：可回退、可测试，语义是「约束」而非破坏契约。

## 验收标准
1. 默认配置下 `POST /api/sites|devices|points` 与对应 `DELETE` → `403 MetadataReadOnly`。
2. `PUT`（改名/补全）与命令写值正常。
3. 自动注册照常：测量数据到达即建站点/设备/点位。
4. 前端管理面板无「新建/删除」入口，保留编辑；大屏写值入口不变。
5. `dotnet build` + `dotnet test` 全绿；`npm run build` 通过。

## 变更记录
- 2026-08-30 新建；用户拍板「站点/设备/点位不应手动新增，但可以写值」。
- 2026-08-30 落地：后端 `Metadata:AllowManualCreate` 开关（默认 false）+ Sites/Devices/Points 的 POST/DELETE 返回 403 MetadataReadOnly；前端三个管理视图去新建/删除入口、保留编辑；新增单测 6 条（403 断言）+ 集成测试 1 条（开关开启时可手动新增/改名 PUT 可用）；`dotnet build`/`dotnet test`/`npm run build` 全绿。
