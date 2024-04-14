import { Routes } from '@angular/router';
import { DevPageComponent } from './pages/dev-page/dev-page.component';
import { OcadVersionerComponent } from './ocad-versioner.component';

export const routes: Routes = [
  { path: '', component: OcadVersionerComponent },
  { path: 'dev', component: DevPageComponent },
  {
    path: 'about',
    loadChildren: () =>
      import('./pages/about-page/about-page.module').then(
        (m) => m.AboutPageModule
      ),
  },
];
