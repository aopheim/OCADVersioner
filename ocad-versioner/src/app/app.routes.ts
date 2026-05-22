import { Routes } from '@angular/router'
import { DevPageComponent } from './pages/dev-page/dev-page.component'
import { OcadVersionerComponent } from './ocad-versioner.component'
import { DiffViewerPageComponent } from './pages/diff-viewer-page/diff-viewer-page.component'

export const routes: Routes = [
    { path: '', component: OcadVersionerComponent },
    { path: 'diff-viewer', component: DiffViewerPageComponent },
    { path: 'dev', component: DevPageComponent },
    {
        path: 'about',
        loadChildren: () =>
            import('./pages/about-page/about-page.module').then(
                (m) => m.AboutPageModule
            ),
    },
]
