import { NgModule } from '@angular/core';
import { OcadDiffTableComponent } from './ocad-diff-table/ocad-diff-table/ocad-diff-table.component';
import { OcadMapViewerComponent } from './ocad-map-viewer/ocad-map-viewer.component';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { ProjectDirectorySelectorComponent } from './project-directory-selector/project-directory-selector.component';
import { DiffTableTabsComponent } from './ocad-diff-table/diff-table-tabs/diff-table-tabs.component';
import { CreateVersionComponent } from './create-version/create-version.component';
import { ReactiveFormsModule } from '@angular/forms';

@NgModule({
  imports: [CommonModule, TranslateModule, ReactiveFormsModule],
  declarations: [
    OcadDiffTableComponent,
    ProjectDirectorySelectorComponent,
    OcadMapViewerComponent,
    DiffTableTabsComponent,
    CreateVersionComponent,
  ],
  exports: [
    OcadDiffTableComponent,
    ProjectDirectorySelectorComponent,
    OcadMapViewerComponent,
    DiffTableTabsComponent,
    CreateVersionComponent,
  ],
})
export class OcadVersionerComponetsModule {}
