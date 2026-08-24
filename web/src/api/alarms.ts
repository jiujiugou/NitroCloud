import client from './client'
import type { AlarmQuery, AlarmRecord, AlarmSummary, ApiResponse } from './types'

/** 告警查询（按站点/级别/状态过滤，ADR-008 D7） */
export async function getAlarms(q: AlarmQuery = {}): Promise<AlarmRecord[]> {
  const { data } = await client.get<ApiResponse<AlarmRecord[]>>('/alarms', { params: q })
  return data.data ?? []
}

/** 告警确认 */
export async function ackAlarm(id: string): Promise<boolean> {
  const { data } = await client.post<ApiResponse<unknown>>(`/alarms/${id}/ack`)
  return data.success
}

/**
 * 告警汇总（KPI：活跃数 / 今日发生数）。
 * 对应后端 AlarmsController.Summary（网关同款），属 ADR-008 D7 清单之外的补充端点，
 * 需后端 M1 同步实现；失败时由调用方降级按告警列表本地统计。
 */
export async function getAlarmSummary(): Promise<AlarmSummary | null> {
  const { data } = await client.get<ApiResponse<AlarmSummary>>('/alarms/summary')
  return data.data ?? null
}
