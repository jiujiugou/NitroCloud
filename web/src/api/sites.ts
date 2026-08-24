import client from './client'
import type { ApiResponse, LatestValue, Site } from './types'

/** 站点列表（含在线状态/最后上报） */
export async function getSites(): Promise<Site[]> {
  const { data } = await client.get<ApiResponse<Site[]>>('/sites')
  return data.data ?? []
}

export async function getSite(id: string): Promise<Site | null> {
  const { data } = await client.get<ApiResponse<Site>>(`/sites/${id}`)
  return data.data ?? null
}

/** 站点最近值快照（ILatestValueCache.GetSite，实时面板秒级读缓存不查库 —— C-005） */
export async function getSiteLatest(siteId: string): Promise<LatestValue[]> {
  const { data } = await client.get<ApiResponse<LatestValue[]>>(`/sites/${siteId}/latest`)
  return data.data ?? []
}

export async function createSite(s: Partial<Site>): Promise<Site | null> {
  const { data } = await client.post<ApiResponse<Site>>('/sites', s)
  return data.data ?? null
}

export async function updateSite(id: string, s: Partial<Site>): Promise<Site | null> {
  const { data } = await client.put<ApiResponse<Site>>(`/sites/${id}`, s)
  return data.data ?? null
}

export async function deleteSite(id: string): Promise<boolean> {
  const { data } = await client.delete<ApiResponse<unknown>>(`/sites/${id}`)
  return data.success
}
