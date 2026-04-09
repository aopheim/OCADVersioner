import { Component } from '@angular/core'
import { CommonModule } from '@angular/common'
import { OcadVersionerComponentsModule } from '../../components/ocad-versioner-components.module'

@Component({
    selector: 'dev-page',
    standalone: true,
    imports: [CommonModule, OcadVersionerComponentsModule],
    templateUrl: './dev-page.component.html',
    styleUrl: './dev-page.component.scss',
})
export class DevPageComponent {}
