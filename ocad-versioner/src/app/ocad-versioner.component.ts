import { Component, OnInit } from '@angular/core';
import {
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
  Position,
} from 'geojson';
import { BehaviorSubject, Observable, combineLatest, filter, map } from 'rxjs';
import { JsonDiffService } from './services/json-diff-service/json-diff-service';
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { OcadDiffTableView } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component';
import { OcadVersionerProvider } from './ocad-versioner.provider';
import { OcadReaderService } from './services/ocad-reader-service/ocad-reader-service';
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper';
import { isNil } from 'lodash-es';
import bbox from '@turf/bbox';
import { CoordinatesHelper } from './services/coordinates-helper/coordinates-helper.service';
import { LoggingService } from './services/logging/logging.service';

@Component({
  selector: 'ocad-versioner',
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  public newestVersion$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public newestVersionMetaData$: BehaviorSubject<VersionMetaData | null> =
    new BehaviorSubject<VersionMetaData | null>(null);
  public oldestVersion$: BehaviorSubject<FeatureCollection | null> =
    new BehaviorSubject<FeatureCollection | null>(null);
  public oldestVersionMetaData$: BehaviorSubject<VersionMetaData | null> =
    new BehaviorSubject<VersionMetaData | null>(null);
  public diffTable$: Observable<OcadDiffDto> = new Observable();
  public selectedTableView$: BehaviorSubject<OcadDiffTableView> =
    new BehaviorSubject<OcadDiffTableView>(OcadDiffTableView.Added);
  public bboxOfNewestVersion$: BehaviorSubject<Position[]> =
    new BehaviorSubject<Position[]>([[]]);

  constructor(
    private jsonDiffService: JsonDiffService,
    public provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService,
    private logging: LoggingService
  ) {
    this.logging.logPageView('ocadversioner.com', 'ocadversioner.com');
  }

  public OcadDiffTableView = OcadDiffTableView;
  ngOnInit(): void {
    this.diffTable$ = combineLatest([
      this.newestVersion$,
      this.oldestVersion$,
    ]).pipe(
      filter(([current, versioned]) => current !== null && versioned !== null),
      map(([current, versioned]) => {
        return this.jsonDiffService.getJsonDiff(versioned!, current!);
      })
    );
  }

  public handleLoadedGeoJsonFile(
    geoJson: FeatureCollection,
    isOriginal: boolean
  ) {
    if (isOriginal) this.newestVersion$.next(geoJson);
    else this.oldestVersion$.next(geoJson);
  }

  public setSelectedTableView(selectedView: OcadDiffTableView): void {
    this.selectedTableView$.next(selectedView);
  }

  public async setSelectedVersion(selectedVersionNumber: number) {
    const newestVersionName =
      OcadDirectoryHelper.getVersionNameFromVersionNumber(
        selectedVersionNumber
      );
    const selectedOcdFile = this.provider.getOcdFileHandle(newestVersionName);
    const newestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      await selectedOcdFile.getFile()
    );
    const bboxOfNewest = this.getBoundingBoxOfFeatureCollection(
      newestFeatureCollection
    );
    this.bboxOfNewestVersion$.next(bboxOfNewest);
    const newestVersionMetaData: VersionMetaData = {
      versionName:
        selectedVersionNumber === 0 ? selectedOcdFile.name : newestVersionName,
      versionNumber: selectedVersionNumber,
    };
    this.newestVersionMetaData$.next(newestVersionMetaData);

    const versionNumberToCompare: number | null =
      this.getVersionNumberToCompare(selectedVersionNumber);
    const ocdFileToCompare = versionNumberToCompare
      ? this.provider.getOcdFileHandle(
          OcadDirectoryHelper.getVersionNameFromVersionNumber(
            versionNumberToCompare
          )
        )
      : null;
    const oldestVersionMetaData: VersionMetaData = {
      versionNumber: versionNumberToCompare ?? 0,
      versionName:
        ocdFileToCompare && !isNil(versionNumberToCompare)
          ? OcadDirectoryHelper.getVersionNameFromVersionNumber(
              versionNumberToCompare
            )
          : '#_empty',
    };
    this.oldestVersionMetaData$.next(oldestVersionMetaData);

    if (!ocdFileToCompare) {
      this.oldestVersion$.next({ features: [], type: 'FeatureCollection' });
      this.newestVersion$.next(newestFeatureCollection);
      return;
    }
    const oldestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      await ocdFileToCompare.getFile()
    );

    this.newestVersion$.next(newestFeatureCollection);
    this.oldestVersion$.next(oldestFeatureCollection);
  }

  private getBoundingBoxOfFeatureCollection(
    featureCollection: FeatureCollection<Geometry, GeoJsonProperties>
  ): Position[] {
    const bboxOfMap = bbox(featureCollection);

    const minLatLon = CoordinatesHelper.getLatLongCoordinateFromUtm(
      bboxOfMap[0],
      bboxOfMap[1],
      32,
      'N'
    );
    const maxLatLon = CoordinatesHelper.getLatLongCoordinateFromUtm(
      bboxOfMap[2],
      bboxOfMap[3],
      32,
      'N'
    );

    return [
      [minLatLon.latitude, minLatLon.longitude],
      [maxLatLon.latitude, maxLatLon.longitude],
    ];
  }

  private getVersionNumberToCompare(
    selectedVersionNumber: number
  ): number | null {
    if (selectedVersionNumber === 0)
      return this.provider.getHighestVersionNumber();
    if (selectedVersionNumber === 1) return null;
    return selectedVersionNumber - 1;
  }
}

interface VersionMetaData {
  versionName: string;
  versionNumber: number;
}
