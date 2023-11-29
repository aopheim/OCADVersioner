import { Component, EventEmitter, Input, Output } from '@angular/core';
import * as ocad2geojson from 'ocad2geojson';
import { FeatureCollection } from 'geojson';
import { from, map } from 'rxjs';
import { Buffer } from 'buffer';

@Component({
  selector: 'ocad-file-uploader',
  templateUrl: './ocad-file-uploader.component.html',
  styleUrl: './ocad-file-uploader.component.scss',
})
export class OcadFileUploaderComponent {
  public fileName: string = '';
  @Input() public title: string = '';
  @Output() public ocadFileAsGeoJson = new EventEmitter<FeatureCollection>();
  constructor() {}

  public onFileSelected(event: Event) {
    const inputEvent = event as HTMLInputEvent;
    const files = inputEvent?.target?.files;
    if (!files || files.length < 1) {
      console.warn('No file found. Exiting.');
      return;
    }
    const file = files[0];
    this.fileName = file.name;
    var reader = new FileReader();

    reader.onload = (e) => {
      const arrayBuffer = reader.result;
      from(ocad2geojson.readOcad(Buffer.from(arrayBuffer as ArrayBuffer)))
        .pipe(
          map((ocadFile) => {
            const geojson: FeatureCollection =
              ocad2geojson.ocadToGeoJson(ocadFile);
            this.ocadFileAsGeoJson.emit(geojson);
          })
        )
        .subscribe();
    };

    reader.readAsArrayBuffer(file);
  }
}

interface HTMLInputEvent extends Event {
  target: HTMLInputElement & EventTarget;
}
