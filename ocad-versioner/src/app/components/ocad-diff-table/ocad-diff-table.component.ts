import { Component, Input } from '@angular/core';
import { OcadDiffDto } from './ocad-diff-table.models';

@Component({
  selector: 'ocad-diff-table',
  templateUrl: './ocad-diff-table.component.html',
  styleUrls: ['./ocad-diff-table.component.scss'],
})
export class OcadDiffTableComponent {
  @Input({ required: true })
  public tableInput?: OcadDiffDto;

  constructor() {}
}
