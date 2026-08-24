// 共享格式化/判定工具（组件与视图复用，ADR-009 D4/D6）
import type { AlarmSeverity, DataType } from '../api/types'

/** ISO 时间 → 本地可读字符串（空值返回 '-'） */
export function fmtTime(iso?: string): string {
  return iso ? new Date(iso).toLocaleString() : '-'
}

/** ISO 时间 → 相对时间（"x 秒前 / x 分钟前 / HH:mm"），空值返回 '--' */
export function timeAgo(iso?: string): string {
  if (!iso) return '--'
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return '--'
  const diff = Date.now() - t
  const s = Math.floor(diff / 1000)
  if (s < 60) return `${s} 秒前`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m} 分钟前`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} 小时前`
  return fmtTime(iso)
}

/** 数值点位类型（Bool 走 switch、String 走 input，其余数值走 input-number —— ADR-009 D6） */
export function isNumericType(t: DataType): boolean {
  return ['Byte', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64', 'UInt64', 'Float', 'Double'].includes(t)
}

/** 尝试把任意值转数值（Bool→0/1、数值字符串→Number），非数值返回 null（非数值点位不上曲线） */
export function toNum(v: unknown): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  if (typeof v === 'boolean') return v ? 1 : 0
  if (typeof v === 'string') {
    const n = Number(v)
    return Number.isFinite(n) ? n : null
  }
  return null
}

/** 点位实时值展示：数值保留两位、Bool 显 ON/OFF、其余字符串化 */
export function fmtValue(v: unknown): string {
  if (typeof v === 'number') return Number.isFinite(v) ? v.toFixed(2) : '--'
  if (typeof v === 'boolean') return v ? 'ON' : 'OFF'
  if (v === null || v === undefined || v === '') return '--'
  return String(v)
}

/** 告警级别 → Element Plus tag type */
export function severityTag(s: AlarmSeverity): 'danger' | 'warning' | 'primary' | 'info' {
  if (s === 'Critical') return 'danger'
  if (s === 'Major') return 'warning'
  if (s === 'Warning') return 'primary'
  return 'info'
}

export function severityText(s: AlarmSeverity): string {
  return { Critical: '严重', Major: '重大', Warning: '警告', Info: '提示' }[s] ?? s
}

/** 告警状态 → Element Plus tag type */
export function stateTag(s: string): 'danger' | 'warning' | 'success' | 'info' {
  if (s === 'Active') return 'danger'
  if (s === 'Acknowledged') return 'warning'
  if (s === 'Resolved' || s === 'Cleared') return 'success'
  return 'info'
}

/** 站点/设备 在线状态 → 中文文本（统一给 admin 和大屏） */
export function statusText(s: string): string {
  return { Online: '在线', Offline: '离线', Unknown: '未知', Maintenance: '维护', Error: '故障' }[s] ?? s
}

/** 站点/设备 在线状态 → Element Plus tag type */
export function onlineTag(s: string): 'success' | 'danger' | 'info' | 'warning' {
  if (s === 'Online') return 'success'
  if (s === 'Offline' || s === 'Error') return 'danger'
  if (s === 'Maintenance') return 'warning'
  return 'info'
}
