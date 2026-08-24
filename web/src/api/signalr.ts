// SignalR Hub 服务（ADR-009 D3：复用网关 signalr.ts 骨架，改站点级订阅）
//
// 后端 NitroCloudHub（ADR-008 D7）：入站 SubscribeSite(siteId)/UnsubscribeSite/JoinGlobal，
// 出站 OnMeasurements/OnAlarm/OnDeviceStatus/OnCommandAck（按 site:{siteId} 分组）。
// 单例连接 + 模块级事件回调，避免大屏多个 composable 各自建连接。
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { ref } from 'vue'

/** 连接状态（供 StatusLight 展示链路状态） */
export const hubConnected = ref(false)

let conn: HubConnection | null = null
let startPromise: Promise<void> | null = null
const subscribedSites = new Set<string>()
/** 重连成功后的回调（订阅方借此补拉该站点 /latest 快照，防白屏） */
const onReconnectedCallbacks: Array<() => void> = []

function build(): HubConnection {
  const c = new HubConnectionBuilder()
    .withUrl('/hubs/cloud')
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect()
    .build()

  c.onreconnected(() => {
    hubConnected.value = true
    // 重连后重发站点级订阅（服务端分组会话已丢）
    subscribedSites.forEach(siteId => {
      c.invoke('SubscribeSite', siteId).catch(() => {})
    })
    onReconnectedCallbacks.forEach(fn => fn())
  })
  c.onclose(() => {
    hubConnected.value = false
  })
  return c
}

/** 获取单例连接（未建时惰性创建；可先挂事件再 start） */
export function getHubConnection(): HubConnection {
  if (!conn) conn = build()
  return conn
}

/** 建立连接（幂等：已连接直接返回） */
export async function startHub(): Promise<void> {
  if (hubConnected.value || conn?.state === 'Connected') return
  const c = getHubConnection()
  if (c.state === 'Connected') {
    hubConnected.value = true
    return
  }
  if (!startPromise) {
    startPromise = c.start()
      .then(() => { hubConnected.value = true })
      .catch(e => { startPromise = null; throw e })
  }
  return startPromise
}

/** 订阅某站点实时推送（服务端 site:{siteId} 分组） */
export function subscribeSite(siteId: string): void {
  subscribedSites.add(siteId)
  if (conn?.state === 'Connected') {
    conn.invoke('SubscribeSite', siteId).catch(() => {})
  }
}

export function unsubscribeSite(siteId: string): void {
  subscribedSites.delete(siteId)
  if (conn?.state === 'Connected') {
    conn.invoke('UnsubscribeSite', siteId).catch(() => {})
  }
}

/** 注册出站事件回调（重复注册需自行去重；大屏单实例使用） */
export function onHubEvent<T>(event: string, handler: (data: T) => void): void {
  getHubConnection().on(event, handler)
}

export function offHubEvent(event: string, handler: (...args: unknown[]) => void): void {
  getHubConnection().off(event, handler)
}

/** 注册重连回调，返回取消函数 */
export function onHubReconnected(fn: () => void): () => void {
  onReconnectedCallbacks.push(fn)
  return () => {
    const i = onReconnectedCallbacks.indexOf(fn)
    if (i >= 0) onReconnectedCallbacks.splice(i, 1)
  }
}

export async function stopHub(): Promise<void> {
  if (conn) {
    await conn.stop()
    hubConnected.value = false
  }
}
