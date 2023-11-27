import { NgModule } from '@angular/core';
import { OcadDiffTableComponent } from './ocad-diff-table/ocad-diff-table.component';
import { OcadFileUploaderComponent } from './ocad-file-uploader/ocad-file-uploader.component';
import { OcadMapViewerComponent } from './ocad-map-viewer/ocad-map-viewer.component';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { LeafletModule } from '@asymmetrik/ngx-leaflet';

@NgModule({
  imports: [CommonModule, TranslateModule, LeafletModule],
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
