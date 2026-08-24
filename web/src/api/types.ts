// NitroCloud 前端领域类型（契约以 DESIGN.md §4 / ADR-008 D7 为准，云侧不单方面改契约）

/** 站点在线状态（ADR-007：离线判定用「最后上报时间 + 阈值」，status 由后端计算） */
export type SiteStatus = 'Online' | 'Offline' | 'Unknown' | 'Maintenance'

export interface Site {
  id: string
  name: string
  location?: string
  status: SiteStatus
  /** 最后上报时间（UTC ISO 字符串），离线判定依据 */
  lastReportAt?: string
  createdAt?: string
}

/** 设备在线状态 */
export type DeviceStatus = 'Online' | 'Offline' | 'Error' | 'Unknown'

export interface Device {
  id: string
  siteId: string
  name: string
  model?: string
  status: DeviceStatus
  /** 最后上报时间（UTC ISO 字符串），设备级在线判定 */
  lastSeenAt?: string
}

/** 点位数据类型（Bool 用 switch、数值用 input-number、String 用 input —— ADR-009 D6 类型感知） */
export type DataType =
  | 'Bool' | 'Byte' | 'Int16' | 'UInt16' | 'Int32' | 'UInt32'
  | 'Int64' | 'UInt64' | 'Float' | 'Double' | 'String'

/** 点位访问权限：仅 ReadOnly 不在 UI 显示写值入口 */
export type PointAccess = 'ReadOnly' | 'WriteOnly' | 'ReadWrite'

export interface Point {
  id: string
  deviceId: string
  name: string
  dataType: DataType
  unit?: string
  alarmEnabled?: boolean
  access?: PointAccess
  enabled?: boolean
}

/** 前端点位行元数据（站点 → 设备 → 点位 三级定位 + 写值类型感知所需字段，ADR-009 D6/D7） */
export interface PointRowMeta {
  siteId: string
  deviceId: string
  pointId: string
  name: string
  dataType: DataType
  unit?: string
  access?: PointAccess
  enabled?: boolean
}

/** 质量标签：quality != Good 的 record 照常入库并打标签，面板灰显（DESIGN.md §4.1） */
export type Quality = 'Good' | 'Uncertain' | 'Bad'

/** 最近值缓存（ILatestValueCache.GetSite，GET /api/sites/{siteId}/latest） */
export interface LatestValue {
  siteId: string
  deviceId: string
  devicePointId: string
  pointName: string
  value: unknown
  dataType?: DataType
  unit?: string
  quality: Quality
  /** 最后上报时间 */
  timestamp: string
}

/** 历史时序点（GET /api/history，ITimeseriesStore 查询结果） */
export interface PointSnapshot {
  siteId: string
  deviceId: string
  devicePointId: string
  value: unknown
  quality: Quality
  timestamp: string
}

export interface HistoryQuery {
  siteId: string
  deviceId?: string
  devicePointId?: string
  from: string
  to: string
  limit?: number
}

export type AlarmSeverity = 'Critical' | 'Major' | 'Warning' | 'Info'
export type AlarmState = 'Active' | 'Acknowledged' | 'Resolved' | 'Cleared'

export interface AlarmRecord {
  id: string
  siteId: string
  deviceId: string
  pointId: string
  ruleId?: string
  severity: AlarmSeverity
  state: AlarmState
  message: string
  triggerValue?: number
  threshold?: number
  occurredAt: string
  ackedAt?: string
}

export interface AlarmQuery {
  siteId?: string
  severity?: AlarmSeverity
  state?: AlarmState
  limit?: number
}

/** 告警汇总（后端 AlarmsController.Summary，KPI：活跃数 / 今日发生数） */
export interface AlarmSummary {
  active: number
  today: number
}

export type CommandStatus = 'Pending' | 'Sent' | 'Acked' | 'Failed' | 'Timeout'

/** 命令记录（ICommandStore，GET /api/commands/{id}） */
export interface CommandRecord {
  id: string
  siteId: string
  deviceId: string
  pointId: string
  type: string
  value: unknown
  status: CommandStatus
  requestedAt: string
  ackedAt?: string
  error?: string
}

/** 云端异步写值请求（POST /api/commands/write，commandId 由后端生成） */
export interface WriteValueRequest {
  siteId: string
  deviceId: string
  pointId: string
  value: unknown
}

export interface WriteValueResponse {
  commandId: string
  status: CommandStatus
  requestedAt: string
}

/** 回执（SignalR OnCommandAck / commands/ack topic，DESIGN.md §4.3） */
export interface CommandAck {
  commandId: string
  result: 'Success' | 'Failed'
  error?: string
  at: string
}

/** 上行测量载荷（DESIGN.md §4.1 BatchMeasurements v1，SignalR OnMeasurements 推送结构） */
export interface MeasurementRecord {
  siteId: string
  deviceId: string
  devicePointId: string
  pointName?: string
  value: unknown
  dataType?: DataType
  unit?: string
  quality: Quality
  timestamp: string
}

/** 通用 API 响应外壳（沿用网关 ApiResponse 形态） */
export interface ApiResponse<T> {
  success: boolean
  data?: T
  error?: { code: string; message: string }
  timestamp: string
}
