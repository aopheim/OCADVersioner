import { RouterModule, Routes } from '@angular/router'
import { AboutPageComponent } from './about-page.component'
import { NgModule } from '@angular/core'
import { AppSettingsService } from '../../services/app-settings-service/app-settings-service'

export const routes: Routes = [{ path: '', component: AboutPageComponent }]

@NgModule({
    imports: [RouterModule.forChild(routes)],
    exports: [RouterModule],
    providers: [AppSettingsService],
})
export class AboutPageRoutingModule {}
