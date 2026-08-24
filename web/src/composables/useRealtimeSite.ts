// 实时订阅协调（ADR-009 D3/D5）：复用 signalr.ts 单例连接，提供站点级订阅与重连补拉。
import { hubConnected, startHub, subscribeSite, unsubscribeSite, onHubReconnected, stopHub } from '../api/signalr'

export function useRealtimeSite() {
  /** 建立连接并订阅一批站点（幂等；失败静默，由 10s KPI 轮询 + 空态兜底） */
  async function connect(siteIds: string[]): Promise<void> {
    try {
      await startHub()
    } catch {
      return
    }
    siteIds.forEach(subscribeSite)
  }

  function disconnect(siteId: string): void {
    unsubscribeSite(siteId)
  }

  /**
   * 重连成功回调：调用方据此补拉该站点 /latest 快照（防白屏，ADR-009 D3）。
   * 返回取消函数。
   */
  function refreshOnReconnect(fn: () => void): () => void {
    return onHubReconnected(fn)
  }

  return { connected: hubConnected, connect, disconnect, refreshOnReconnect, stopHub }
}
