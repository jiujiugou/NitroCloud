// 认证会话（ADR-015 一层认证）：模块级响应式状态，不引 Pinia。
// localStorage 持久化（token/username/role），client.ts 请求拦截器读取同一份。
import { computed, ref } from 'vue'

export interface AuthSession {
  token: string
  username: string
  role: string
}

const TOKEN_KEY = 'token'
const USERNAME_KEY = 'username'
const ROLE_KEY = 'role'

/** 从 localStorage 恢复会话（刷新后保持登录态） */
function loadSession(): AuthSession | null {
  const token = localStorage.getItem(TOKEN_KEY)
  if (!token) return null
  return {
    token,
    username: localStorage.getItem(USERNAME_KEY) ?? '',
    role: localStorage.getItem(ROLE_KEY) ?? ''
  }
}

export const session = ref<AuthSession | null>(loadSession())

/**
 * 登录成功写入会话（登录页/其他全局场景调用）。
 * 同时写入 localStorage 的 token/username/role，与 client.ts 请求拦截器读取同一份。
 */
export function setSession(s: AuthSession): void {
  localStorage.setItem(TOKEN_KEY, s.token)
  localStorage.setItem(USERNAME_KEY, s.username)
  localStorage.setItem(ROLE_KEY, s.role)
  session.value = s
}

/** 退出 / 401 清理会话（顶栏退出、client.ts 401 处理调用）。 */
export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USERNAME_KEY)
  localStorage.removeItem(ROLE_KEY)
  session.value = null
}

/**
 * 认证状态 composable（模块级单例，所有调用方共享同一份会话）。
 * @returns isAuthenticated 是否已登录；setSession 登录成功写入；clearSession 退出清理。
 */
export function useAuth() {
  const isAuthenticated = computed(() => session.value !== null)
  return { session, isAuthenticated, setSession, clearSession }
}
