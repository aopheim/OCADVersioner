import { Component, EventEmitter, Input, Output } from '@angular/core'
import { OcadDiffTableView } from '../ocad-diff-table/ocad-diff-table.component'
import { OcadDiffDto } from '../ocad-diff-table/ocad-diff-table.models'

@Component({
    selector: 'diff-table-tabs',
    templateUrl: './diff-table-tabs.component.html',
    styleUrl: './diff-table-tabs.component.scss',
})
export class DiffTableTabsComponent {
    public OcadDiffTableView = OcadDiffTableView
    @Input()
    public diffTable: OcadDiffDto | null = null
    @Output()
    public selectedTableView: EventEmitter<OcadDiffTableView> =
        new EventEmitter()

    public currentView: OcadDiffTableView = OcadDiffTableView.Added
    public setSelectedTableView(selectedView: OcadDiffTableView): void {
        this.currentView = selectedView
        this.selectedTableView.emit(selectedView)
    }
}
