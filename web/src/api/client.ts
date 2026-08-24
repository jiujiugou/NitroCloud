import axios from 'axios'
import { ElMessage } from 'element-plus'

const client = axios.create({
  // ADR-009：相对路径，dev 走 Vite 代理（/api → 127.0.0.1:5100），生产走 nginx /api/ 反代；
  // 不写死后端地址，避免生产部署下浏览器直连自身 localhost。
  baseURL: '/api',
  timeout: 10000,
  headers: { 'Content-Type': 'application/json' }
})

// 请求拦截器：预留鉴权（初版无登录，Token 注入逻辑后续演进）
client.interceptors.request.use(config => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// 响应拦截器：统一错误提示（401 预留跳登录）
client.interceptors.response.use(
  r => r,
  err => {
    console.error('API Error:', err.message)
    if (err.response?.status === 401) {
      localStorage.removeItem('token')
    } else if (err.response?.status === 403) {
      ElMessage.error(err.response?.data?.error?.message ?? '无权限执行该操作')
    } else if (err.response?.data?.error?.message) {
      ElMessage.error(err.response.data.error.message)
    } else if (err.code === 'ERR_NETWORK') {
      // 后端（NitroCloud.Api）未启动时的兜底提示，避免静默失败
      ElMessage.warning('无法连接后端服务')
    }
    return Promise.reject(err)
  }
)

export default client
