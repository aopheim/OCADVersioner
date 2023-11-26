import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GeoJsonObject } from 'geojson';
import { BehaviorSubject, Observable, combineLatest, filter, map, of } from 'rxjs';
import { OcadFileUploaderComponent } from './components/ocad-file-uploader/ocad-file-uploader.component';
import { JsonDiffService } from './services/json-diff-service';
import { OcadMapViewerComponent } from './components/ocad-map-viewer/ocad-map-viewer.component';
import { OcadVersionerModule } from './ocad-versioner.module';
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table.models';

@Component({
  selector: 'ocad-versioner',
  standalone: true,
  imports: [CommonModule, OcadVersionerModule, OcadFileUploaderComponent],
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  public originalAsGeoJson$: BehaviorSubject<GeoJsonObject | null> =
    new BehaviorSubject<GeoJsonObject | null>(null);
  public versionedAsGeoJson$: BehaviorSubject<GeoJsonObject | null> =
    new BehaviorSubject<GeoJsonObject | null>(null);
  public diffTable$: Observable<OcadDiffDto> = new Observable();
  constructor(private jsonDiffService: JsonDiffService) {}


  ngOnInit(): void {
    combineLatest([this.originalAsGeoJson$, this.versionedAsGeoJson$])
      .pipe(
        filter(
          ([current, versioned]) => current !== null && versioned !== null
        ),
        map(([current, versioned]) => {
          const diff = this.jsonDiffService.getJsonDiff(versioned, current);
          console.log('diff: ', diff);
        })
      )
      .subscribe();
      this.diffTable$ = of(this.mockDiffTable())
  }

  public handleLoadedGeoJsonFile(geoJson: GeoJsonObject, isOriginal: boolean) {
    if (isOriginal) this.originalAsGeoJson$.next(geoJson);
    else this.versionedAsGeoJson$.next(geoJson);
  }

  private mockDiffTable(): OcadDiffDto {
    return {
      added: [{
        symbolName: 'Høydepunkt',
        symbolNumber: '123.34',
        createdAtUtc: new Date(),
        lastEditBy: 'Adrian Opheim',
        lastEditedAtUtc: new Date(),
      },
      {
        symbolName: 'Kolle',
        symbolNumber: '124.34',
        createdAtUtc: new Date(),
        lastEditBy: 'Adrian Opheim',
        lastEditedAtUtc: new Date(),
      },
      {
        symbolName: 'Stein',
        symbolNumber: '125.34',
        createdAtUtc: new Date(),
        lastEditBy: 'Adrian Opheim',
        lastEditedAtUtc: new Date(),
      }
    ],
    edited: [
      {
        areaSymbolDiff: {areaDiffInPercent: -20,},
        createdAtUtc: new Date(),
        symbolName: 'Jorde',
        symbolNumber: '145.44',
        lastEditBy: 'Adrian Opheim',
        lastEditedAtUtc: new Date(),
    },
    {
      lineSymbolDiff: {
        lengthDiffInPercent: 14
      },
      createdAtUtc: new Date(),
      symbolName: 'Sti',
      symbolNumber: '145.44',
      lastEditBy: 'Adrian Opheim',
      lastEditedAtUtc: new Date(),
    },
    {
      pointSymbolDiff: {movementInMeters: 20},
      createdAtUtc: new Date(),
      symbolName: 'Stein',
      symbolNumber: '145.44',
      lastEditBy: 'Adrian Opheim',
      lastEditedAtUtc: new Date(),
    },
],
    deleted: [{
      createdAtUtc: new Date(),
      symbolName: 'Høydekurve',
      symbolNumber: '156.21',
      lastEditBy: 'Adrian Opheim',
      lastEditedAtUtc: new Date()
    }]

    } as OcadDiffDto;
  }

}

