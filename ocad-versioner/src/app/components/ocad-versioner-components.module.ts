import { NgModule } from '@angular/core';
import { OcadDiffTableComponent } from './ocad-diff-table/ocad-diff-table.component';
import { OcadFileUploaderComponent } from './ocad-file-uploader/ocad-file-uploader.component';
import { OcadMapViewerComponent } from './ocad-map-viewer/ocad-map-viewer.component';
import { CommonModule } from '@angular/common';

@NgModule({
  imports: [CommonModule],
  declarations: [
    OcadDiffTableComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
  ],
  exports: [
    OcadDiffTableComponent,
    OcadFileUploaderComponent,
    OcadMapViewerComponent,
  ],
})
export class OcadVersionerComponetsModule {}
