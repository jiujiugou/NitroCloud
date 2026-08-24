import client from './client'
import type { ApiResponse, Point } from './types'

/** 设备下点位列表 */
export async function getDevicePoints(deviceId: string): Promise<Point[]> {
  const { data } = await client.get<ApiResponse<Point[]>>(`/devices/${deviceId}/points`)
  return data.data ?? []
}

export async function createPoint(deviceId: string, p: Partial<Point>): Promise<Point | null> {
  const { data } = await client.post<ApiResponse<Point>>(`/devices/${deviceId}/points`, p)
  return data.data ?? null
}

export async function updatePoint(deviceId: string, pointId: string, p: Partial<Point>): Promise<Point | null> {
  const { data } = await client.put<ApiResponse<Point>>(`/devices/${deviceId}/points/${pointId}`, p)
  return data.data ?? null
}

export async function deletePoint(deviceId: string, pointId: string): Promise<boolean> {
  const { data } = await client.delete<ApiResponse<unknown>>(`/devices/${deviceId}/points/${pointId}`)
  return data.success
}
