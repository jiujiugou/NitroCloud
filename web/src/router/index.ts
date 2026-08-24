import { createRouter, createWebHistory } from 'vue-router'

// ADR-009 D1/D2：两条入口——`/dashboard` 大屏（深色只读）+ `/admin/*` 管理面板（浅色 CRUD）。
// 扁平路由（不套无组件父路由），由 App.vue 依据 meta.layout 决定是否渲染管理面板外壳。
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/dashboard' },
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

export default router
