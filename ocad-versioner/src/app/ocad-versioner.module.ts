import { NgModule } from '@angular/core';
import { OcadVersionerComponent } from './ocad-versioner.component';
import { JsonDiffService } from './services/json-diff-service';
// import { LeafletModule } from '@asymmetrik/ngx-leaflet';
import { OcadVersionerComponetsModule as OcadVersionerComponentsModule } from './components/ocad-versioner-components.module';
import { CommonModule } from '@angular/common';

@NgModule({
  imports: [OcadVersionerComponentsModule, CommonModule],
  declarations: [OcadVersionerComponent],
  exports: [OcadVersionerComponent],
  providers: [JsonDiffService],
})
export class OcadVersionerModule {}
