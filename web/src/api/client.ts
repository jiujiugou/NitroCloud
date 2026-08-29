import axios from 'axios'
import { ElMessage } from 'element-plus'
import { clearSession } from '../auth/useAuth'

const client = axios.create({
  // ADR-009：相对路径，dev 走 Vite 代理（/api → 127.0.0.1:5100），生产走 nginx /api/ 反代；
  // 不写死后端地址，避免生产部署下浏览器直连自身 localhost。
  baseURL: '/api',
  timeout: 10000,
  headers: { 'Content-Type': 'application/json' }
})

// 请求拦截器：Bearer Token 注入（ADR-015，登录后由 localStorage 携带）。
client.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：统一错误提示；401 区分「登录失败」与「会话失效」。
// 用 window.location.assign 跳转而非 router，避免 client ↔ router 循环依赖。
client.interceptors.response.use(
  r => r,
  err => {
    console.error('API Error:', err.message)
    if (err.response?.status === 401) {
      const onLoginPage = window.location.pathname.startsWith('/login')
      if (onLoginPage) {
        // 登录失败：展示后端错误（如「用户名或密码错误」），不跳转避免无限循环。
        ElMessage.error(err.response?.data?.error?.message ?? '用户名或密码错误')
      } else {
        // 会话失效/未登录：清理本地会话并回登录页（保留原路径便于登录后跳回）。
        clearSession()
        const current = window.location.pathname + window.location.search
        window.location.assign(`/login?redirect=${encodeURIComponent(current)}`)
      }
    } else if (err.response?.status === 403) {
      ElMessage.error(err.response?.data?.error?.message ?? '无权限执行该操作')
    } else if (err.response?.data?.error?.message) {
      ElMessage.error(err.response.data.error.message)
    } else if (err.code === 'ERR_NETWORK') {
      // 后端（NitroCloud.Api）未启动时的兜底提示，避免静默失败。
      ElMessage.warning('无法连接后端服务')
    }
    return Promise.reject(err)
  }
)

export default client
