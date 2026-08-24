import client from './client'
import type { ApiResponse, CommandRecord, WriteValueRequest, WriteValueResponse } from './types'

/** 发起云端异步写值（POST /api/commands/write，commandId 由后端生成；回执走 SignalR OnCommandAck） */
export async function writeValue(req: WriteValueRequest): Promise<WriteValueResponse | null> {
  const { data } = await client.post<ApiResponse<WriteValueResponse>>('/commands/write', req)
  return data.data ?? null
}

/** 命令状态查询（回执超时兜底轮询用） */
export async function getCommand(commandId: string): Promise<CommandRecord | null> {
  const { data } = await client.get<ApiResponse<CommandRecord>>(`/commands/${commandId}`)
  return data.data ?? null
}
