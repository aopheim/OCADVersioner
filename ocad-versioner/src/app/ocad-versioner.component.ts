import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GeoJsonObject } from 'geojson';
import {
  BehaviorSubject,
  Observable,
  combineLatest,
  filter,
  map,
  of,
} from 'rxjs';
import { OcadFileUploaderComponent } from './components/ocad-file-uploader/ocad-file-uploader.component';

@Component({
  selector: 'ocad-versioner',
  standalone: true,
  imports: [CommonModule, OcadFileUploaderComponent],
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  public originalAsGeoJson$: BehaviorSubject<GeoJsonObject | null> =
    new BehaviorSubject<GeoJsonObject | null>(null);
  public versionedAsGeoJson$: BehaviorSubject<GeoJsonObject | null> =
    new BehaviorSubject<GeoJsonObject | null>(null);

  ngOnInit(): void {
    combineLatest([this.originalAsGeoJson$, this.versionedAsGeoJson$])
      .pipe(
        filter(
          ([original, versioned]) => original !== null && versioned !== null
        ),
        map(([original, versioned]) => {
          console.log('original: ', original);
          console.log('versioned: ', versioned);
        })
      )
      .subscribe();
  }

  public handleLoadedGeoJsonFile(geoJson: GeoJsonObject, isOriginal: boolean) {
    if (isOriginal) this.originalAsGeoJson$.next(geoJson);
    else this.versionedAsGeoJson$.next(geoJson);
  }
}
