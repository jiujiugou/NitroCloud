import client from './client'
import type { ApiResponse, Device } from './types'

/** 站点下设备列表 */
export async function getSiteDevices(siteId: string): Promise<Device[]> {
  const { data } = await client.get<ApiResponse<Device[]>>(`/sites/${siteId}/devices`)
  return data.data ?? []
}

export async function getDevice(id: string): Promise<Device | null> {
  const { data } = await client.get<ApiResponse<Device>>(`/devices/${id}`)
  return data.data ?? null
}

export async function createDevice(d: Partial<Device>): Promise<Device | null> {
  const { data } = await client.post<ApiResponse<Device>>('/devices', d)
  return data.data ?? null
}

export async function updateDevice(id: string, d: Partial<Device>): Promise<Device | null> {
  const { data } = await client.put<ApiResponse<Device>>(`/devices/${id}`, d)
  return data.data ?? null
}

export async function deleteDevice(id: string): Promise<boolean> {
  const { data } = await client.delete<ApiResponse<unknown>>(`/devices/${id}`)
  return data.success
}

/** 全部设备（管理面板点位视图按设备过滤用；初版无分页） */
export async function getAllDevices(): Promise<Device[]> {
  const { data } = await client.get<ApiResponse<Device[]>>('/devices')
  return data.data ?? []
}
