import { Component, EventEmitter, Input, Output } from '@angular/core'
import { OcadVersionListItemDto as OcadVersionListItemDto } from './ocad-version-list.models'

@Component({
    selector: 'ocad-version-list',
    templateUrl: './ocad-version-list.component.html',
    styleUrl: './ocad-version-list.component.scss',
})
export class OcadVersionListComponent {
    @Output()
    public versionNumberSelected: EventEmitter<number> =
        new EventEmitter<number>()

    // 0 indicates the current version
    private _selectedVersionNumber: number = 0

    @Input({ required: true })
    public releases: OcadVersionListItemDto[] = []

    public setSelectedVersionNumber(versionNumber: number) {
        if (this._selectedVersionNumber === versionNumber) versionNumber = 0
        this._selectedVersionNumber = versionNumber
        this.versionNumberSelected.emit(versionNumber)
    }

    public getSelectedVersionNumber(): number {
        return this._selectedVersionNumber
    }
}
