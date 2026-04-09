import { Component, Input } from '@angular/core'
import {
    AddedSymbolDto,
    DeletedSymbolDto,
    EditedSymbolDto,
    OcadDiffDto,
} from './ocad-diff-table.models'

@Component({
    selector: 'ocad-diff-table',
    templateUrl: './ocad-diff-table.component.html',
    styleUrls: ['./ocad-diff-table.component.scss'],
})
export class OcadDiffTableComponent {
    @Input({ required: true })
    public tableInput?: OcadDiffDto | null

    @Input({ required: true })
    public tableView: OcadDiffTableView = OcadDiffTableView.NotSet
    public OcadDiffTableView = OcadDiffTableView
    constructor() {}

    public getAddedOrDeletedSymbols(): AddedSymbolDto[] | DeletedSymbolDto[] {
        if (!this.tableInput) return []
        switch (this.tableView) {
            case OcadDiffTableView.Added:
                return this.tableInput.added
            case OcadDiffTableView.Deleted:
                return this.tableInput.deleted
            default:
                return []
        }
    }

    public getUpdatedSymbols(): EditedSymbolDto[] {
        return this.tableInput?.edited ?? []
    }
}

export enum OcadDiffTableView {
    NotSet = 0,
    Added = 1,
    Updated = 2,
    Deleted = 3,
}
