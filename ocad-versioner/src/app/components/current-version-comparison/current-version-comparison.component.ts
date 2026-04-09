import { Component, Input } from '@angular/core'

@Component({
    selector: 'current-version-comparison',
    templateUrl: './current-version-comparison.component.html',
    styleUrl: './current-version-comparison.component.scss',
})
export class CurrentVersionComparisonComponent {
    @Input({ required: true })
    public newestVersionName: string | null = null
    @Input({ required: true })
    public oldestVersionName: string | null = null

    @Input({ required: true })
    public isHistory: boolean = true
}
