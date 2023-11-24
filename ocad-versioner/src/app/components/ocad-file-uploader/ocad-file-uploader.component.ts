import { Component, EventEmitter, Input, NgZone, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as ocad2geojson from 'ocad2geojson';
import { Buffer } from 'buffer';
import { GeoJsonObject } from 'geojson';
import { from, map } from 'rxjs';

@Component({
  selector: 'ocad-file-uploader',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './ocad-file-uploader.component.html',
  styleUrl: './ocad-file-uploader.component.scss',
})
export class OcadFileUploaderComponent {
  public fileName: string = '';
  @Input() public title: string = '';
  @Output() public ocadFileAsGeoJson = new EventEmitter<GeoJsonObject>();
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
            const geojson: GeoJsonObject = ocad2geojson.ocadToGeoJson(ocadFile);
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
