# NitroCloud Web（Vue 3 大屏 + 管理面板）

依据 `notes/ADR/web/ADR-009-frontend-initial-design.md` 实现的前端初版。

## 运行

```bash
npm install
npm run dev      # http://localhost:5173  （/api 与 /hubs 代理到 127.0.0.1:5100）
npm run build    # vue-tsc 类型检查 + vite 产物
```

## 页面

- `/dashboard` — 大屏（深色只读）：KPI / 站点总览 / 实时曲线 / 告警滚动 / 写值闭环
- `/admin/*` — 管理面板（浅色 CRUD）：站点 / 设备 / 点位 / 告警 / 历史

> 依赖后端 `NitroCloud.Api`（M1 未开工前 REST/SignalR 请求会失败，页面以空态兜底）。
