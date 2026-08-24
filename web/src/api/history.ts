import client from './client'
import type { ApiResponse, HistoryQuery, PointSnapshot } from './types'

/** 时序查询（GET /api/history，ITimeseriesStore.QueryAsync） */
export async function getHistory(q: HistoryQuery): Promise<PointSnapshot[]> {
  const { data } = await client.get<ApiResponse<PointSnapshot[]>>('/history', { params: q })
  return data.data ?? []
}

/** CSV 导出（GET /api/history/export，blob 下载） */
export async function exportHistory(q: HistoryQuery): Promise<void> {
  const r = await client.get('/history/export', { params: q, responseType: 'blob' })
  const url = URL.createObjectURL(new Blob([r.data]))
  const a = document.createElement('a')
  a.href = url
  a.download = `history_${q.siteId}_${q.devicePointId ?? 'all'}.csv`
  a.click()
  URL.revokeObjectURL(url)
}
