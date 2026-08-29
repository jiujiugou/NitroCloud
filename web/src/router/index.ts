import { createRouter, createWebHistory } from 'vue-router'
import { session } from '../auth/useAuth'

// ADR-009 D1/D2：两条入口——`/dashboard` 大屏（深色只读）+ `/admin/*` 管理面板（浅色 CRUD）。
// 扁平路由（不套无组件父路由），由 App.vue 依据 meta.layout 决定是否渲染管理面板外壳。
// ADR-015：一层认证——管理面板需登录（守卫跳 /login），登录页已登录则回管理面板，大屏保持匿名。
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/dashboard' },
    {
      path: '/login',
      name: 'Login',
      component: () => import('../views/LoginView.vue'),
      meta: { layout: 'auth', title: '登录' }
    },
    {
      path: '/dashboard',
      name: 'Dashboard',
      component: () => import('../views/dashboard/DashboardView.vue'),
      meta: { layout: 'dashboard', title: '实时大屏' }
    },
    {
      path: '/admin/sites',
      name: 'AdminSites',
      component: () => import('../views/admin/SitesView.vue'),
      meta: { layout: 'admin', title: '站点管理' }
    },
    {
      path: '/admin/devices',
      name: 'AdminDevices',
      component: () => import('../views/admin/DevicesView.vue'),
      meta: { layout: 'admin', title: '设备管理' }
    },
    {
      path: '/admin/points',
      name: 'AdminPoints',
      component: () => import('../views/admin/PointsView.vue'),
      meta: { layout: 'admin', title: '点位管理' }
    },
    {
      path: '/admin/alarms',
      name: 'AdminAlarms',
      component: () => import('../views/admin/AlarmsView.vue'),
      meta: { layout: 'admin', title: '告警记录' }
    },
    {
      path: '/admin/history',
      name: 'AdminHistory',
      component: () => import('../views/admin/HistoryView.vue'),
      meta: { layout: 'admin', title: '历史数据' }
    }
  ]
})

// 路由守卫：管理面板未登录 → /login（带 redirect 原路径）；已登录访问 /login → 回管理面板。
router.beforeEach(to => {
  const loggedIn = session.value !== null
  if (to.meta.layout === 'admin' && !loggedIn) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }
  if (to.path === '/login' && loggedIn) {
    return { path: '/admin/sites' }
  }
  return true
})

export default router
