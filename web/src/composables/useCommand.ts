// 云端异步写值（ADR-009 D6）：POST /api/commands/write → SignalR OnCommandAck 回执，
// 超时（默认 10s）后轮询命令状态兜底 → 返回最终状态给调用方 toast。
import { ref } from 'vue'
import { writeValue, getCommand } from '../api/commands'
import { onHubEvent } from '../api/signalr'
import type { CommandAck, CommandStatus, WriteValueRequest } from '../api/types'

const ACK_TIMEOUT_MS = 10_000

/** commandId → 等待中的 resolve 与定时器（模块级，多个实例共享同一回执流） */
const ackWaiters = new Map<string, { resolve: (s: CommandStatus) => void; timer: number }>()

let registered = false

function resolveAck(ack: CommandAck): void {
  const w = ackWaiters.get(ack.commandId)
  if (!w) return
  window.clearTimeout(w.timer)
  ackWaiters.delete(ack.commandId)
  w.resolve(ack.result === 'Success' ? 'Acked' : 'Failed')
}

if (!registered) {
  registered = true
  onHubEvent<CommandAck>('OnCommandAck', resolveAck)
}

export function useCommand() {
  /** 是否有写值请求在途（写值按钮 loading） */
  const sending = ref(false)
  const pendingCount = ref(0)

  /**
   * 发起写值并等待回执。
   * @returns 最终状态：Acked / Failed / Timeout / Error（Error = 提交阶段网络/后端失败）
   */
  async function send(req: WriteValueRequest): Promise<CommandStatus | 'Error'> {
    sending.value = true
    try {
      const resp = await writeValue(req)
      if (!resp?.commandId) return 'Error'

      pendingCount.value++
      return await new Promise<CommandStatus | 'Error'>(resolve => {
        const timer = window.setTimeout(async () => {
          ackWaiters.delete(resp.commandId)
          // 超时兜底：轮询命令状态一次（ADR-009 D6）
          try {
            const cmd = await getCommand(resp.commandId)
            if (cmd?.status === 'Acked' || cmd?.status === 'Failed') resolve(cmd.status)
            else resolve('Timeout')
          } catch {
            resolve('Timeout')
          }
        }, ACK_TIMEOUT_MS)
        ackWaiters.set(resp.commandId, { resolve, timer })
      })
    } catch {
      return 'Error'
    } finally {
      sending.value = false
      pendingCount.value = Math.max(0, pendingCount.value - 1)
    }
  }

  return { sending, pendingCount, send }
}
