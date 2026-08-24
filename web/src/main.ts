import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import './styles/base.css'
import './styles/theme-dark.css'
import './styles/theme-light.css'
import App from './App.vue'
import router from './router'

// ADR-009 D1/D5：全局挂 Element Plus + Router；不引 Pinia（大屏/管理面板各用 composables 局部状态）。
const app = createApp(App)
app.use(ElementPlus)
app.use(router)
app.mount('#app')
