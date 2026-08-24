# ADR-009：前端初版设计（大屏 + 管理面板 · 实时/曲线/写值）

## 问题
DESIGN.md 只写"web/ Vue3 大屏 + 管理面板"，缺可落地的初版结构：页面形态、目录、实时数据策略、与网关 web 的复用边界。

## 范围
覆盖 `web/` 目录结构、页面形态（大屏/管理面板）、实时订阅与曲线、反向写值 UI。作为 M3~M5 前端初版基线（草案待评审）。

## 设计决策

### D1 页面形态与主题
- 同仓同框架：Vue 3 + Vite + TypeScript + Element Plus + ECharts + `@microsoft/signalr` + axios（沿用网关）。
- 两套视图：`/dashboard`（大屏，深色只读，一屏看全部站点）；`/admin`（管理面板，浅色交互 CRUD）。
- 主题走 CSS variables（`theme-dark.css` / `theme-light.css`），组件用 var 不写死颜色。

### D2 目录结构
```
web/src/
  main.ts / App.vue / router/index.ts
  api/       client.ts(axios) types.ts signalr.ts
             sites.ts devices.ts points.ts history.ts alarms.ts commands.ts
  composables/  useRealtimeSite() useLatestValues() useAlarmFeed() useCommand()
  components/   KpiCard.vue SiteCard.vue PointRow.vue RealtimeChart.vue
                AlarmTicker.vue CommandDialog.vue StatusLight.vue
  views/
    dashboard/  DashboardView.vue     // 大屏
    admin/      SitesView DevicesView PointsView AlarmsView HistoryView
  styles/       theme-dark.css theme-light.css base.css
```

### D3 实时数据策略
- 首屏：REST 拉 `/api/sites` + `/api/sites/{id}/latest` + `/api/alarms`（快照）。
- 增量：SignalR `SubscribeSite(siteId)` → `OnMeasurements/OnAlarm/OnDeviceStatus/OnCommandAck`。
- 重连：`onreconnected` 后重发订阅 + 补拉该站点 `/latest`（防白屏），复用网关 `signalr.ts` 骨架，改站点级订阅。
- KPI（站点数/在线率/今日告警）10s 轮询 + SignalR 兜底。

### D4 实时曲线（复用网关）
- ECharts 按需引入（Line + Grid/Tooltip + Canvas），抽 `RealtimeChart.vue`。
- 预载 2h 历史（`/api/history`）+ SignalR 追加滚动，环形缓冲上限 7200 点，500ms 节流重绘。
- `step:'end'` 防长静默段假连线；非数值点位不上曲线。
- 大屏曲线支持 站点 → 设备 → 点位 三级选择（联动 `SiteCard`）。

### D5 状态管理
- 初版**不引 Pinia**：大屏只读，用 composables（reactive Map 存 `siteId → 站点/最近值/告警 feed`）即可。
- 管理面板 CRUD 用局部状态；若跨页面共享复杂状态再引入（待验证，可回退）。

### D6 反向写值 UI
- `PointRow` 行尾"写值"→ `CommandDialog`（类型感知：Bool 用 switch、数值用 input-number、String 用 input）→ POST `/api/commands/write`。
- 回执反馈：SignalR `OnCommandAck` 或轮询命令状态 → toast 成功/失败/超时。
- 复用网关写值弹窗交互，但改为**云端异步回执**（非本地直写）。

### D7 大屏布局（CSS Grid 三段式，16:9 自适应）
```
顶部条: 标题 + StatusLight(链路状态) + 全局 KPI
中左:   SiteCard 列表（在线/离线/最后上报/告警数，点击联动曲线）
中右:   RealtimeChart 曲线（三级选择）
右栏:   AlarmTicker 告警滚动（级别着色 + 站点前缀）
左下方: PointRow 快照（实时值/质量灰显/写值按钮）
```

### D8 复用边界（直接抄网关的）
| 内容 | 网关来源 | 处理 |
| --- | --- | --- |
| `signalr.ts` 连接/重连/订阅 | `web/src/api/signalr.ts` | 抄骨架，改站点级订阅 |
| ECharts 曲线配置 | `MonitoringView` | 抄配置，抽 `RealtimeChart.vue` |
| 写值弹窗交互 | `MonitoringView` | 抄交互，改云端异步回执 |
| KPI 卡/告警表样式 | `DashboardView` | 抄样式，改多站点聚合 |
| 离线灰显 `isStale` 逻辑 | `MonitoringView` | 抄逻辑 |

## 载荷墙
- 大屏只做 5 块核心（KPI / 站点总览 / 实时曲线 / 告警滚动 / 写值闭环），**不做管理 CRUD、不做装饰动效**（3D/滚动字幕/粒子）。
- 契约以网关侧为准，前端不单方面改契约。
- 组件从网关 web 复制后按本目录重组，不引入网关无关依赖。

## 待验证 / 开放问题
- [ ] 不引 Pinia 的方案在"大屏多站点 + 管理面板并存"时是否够用（M3 验证，不够再回退）。
- [ ] SignalR 站点级分组 vs 全量推送的量级（N 网关演示下先分组）。
- [ ] 大屏 16:9 固定（1920×1080） vs 自适应——演示大屏建议固定，待定。

## 变更记录
- 2026-08-23 新建，状态：草案待评审（M3 前评审，评审后同步 DESIGN.md）。
- 2026-08-23 前端初版已按本草案落地（`web/` 骨架 + 大屏 + 管理面板五视图），`npm run build` 通过；状态：草案 → 已实现，待与后端 M1 联调后正式评审并同步 DESIGN.md。
