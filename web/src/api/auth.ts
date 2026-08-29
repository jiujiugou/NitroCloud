import client from './client'
import type { ApiResponse, LoginRequest, LoginResponse } from './types'

/** 登录（POST /api/auth/login，ADR-015；失败由 client.ts 统一提示） */
export async function login(req: LoginRequest): Promise<LoginResponse | null> {
  const { data } = await client.post<ApiResponse<LoginResponse>>('/auth/login', req)
  return data.data ?? null
}
