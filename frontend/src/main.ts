import { createApp } from 'vue'
import './styles/app.css'
import './styles/docx-viewer.css'
import './styles/chart.css'
import App from './App.vue'
import router from './router'

createApp(App).use(router).mount('#app')
