import { Component, EventEmitter, Output } from '@angular/core';
import { OcadDiffTableView } from '../ocad-diff-table/ocad-diff-table.component';

@Component({
  selector: 'diff-table-tabs',
  templateUrl: './diff-table-tabs.component.html',
  styleUrl: './diff-table-tabs.component.scss',
})
export class DiffTableTabsComponent {
  public OcadDiffTableView = OcadDiffTableView;
  @Output()
  public selectedTableView: EventEmitter<OcadDiffTableView> =
    new EventEmitter();

  public currentView: OcadDiffTableView = OcadDiffTableView.Added;
  public setSelectedTableView(selectedView: OcadDiffTableView): void {
    this.selectedTableView.emit(selectedView);
  }
}
