import { Component, OnInit } from '@angular/core';
import {
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
  Position,
} from 'geojson';
import {
  BehaviorSubject,
  Observable,
  distinctUntilChanged,
  filter,
  map,
  of,
  withLatestFrom,
} from 'rxjs';
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { OcadDiffTableView } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component';
import { OcadVersionerProvider } from './ocad-versioner.provider';
import { OcadReaderService } from './services/ocad-reader-service/ocad-reader-service';
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper';
import { isNil } from 'lodash-es';
import bbox from '@turf/bbox';
import { CoordinatesHelper } from './services/coordinates-helper/coordinates-helper.service';
import { LoggingService } from './services/logging/logging.service';
import { ProgressIndicatorService } from './services/progress-indicator-service/progress-indicator.service';
import { JsonDiffServiceInput } from './services/json-diff-service/json-diff-service.models';
import { AppProgress } from './services/progress-indicator-service/progress-indicator.service.models';
import { SelectedVersionNumberDto } from './components/ocad-map-viewer/ocad-map-viewer.component';
import { FileWatcherService } from './services/file-watcher/file-watcher.service';
import { AppSettingsService } from './services/app-settings-service/app-settings-service';
import { AppSettings } from './services/app-settings-service/app-settings.models';

@Component({
  selector: 'ocad-versioner',
  templateUrl: './ocad-versioner.component.html',
  styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
  private _jsonDiffWorker: Worker | null;
  private _currentAppSettings: AppSettings = {};
  private triggerJsonDiff$: BehaviorSubject<boolean> =
    new BehaviorSubject<boolean>(false);
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

  public appProgress$: Observable<AppProgress> =
    this.progressService.getAppProgress();

  public selectedVersionNumbers$: BehaviorSubject<SelectedVersionNumberDto | null> =
    new BehaviorSubject<SelectedVersionNumberDto | null>(null);

  constructor(
    public provider: OcadVersionerProvider,
    private ocadReader: OcadReaderService,
    private logger: LoggingService,
    private progressService: ProgressIndicatorService,
    private fileWatcherService: FileWatcherService,
    appSettings: AppSettingsService
  ) {
    this.logger.logPageView('ocadversioner.com', 'ocadversioner.com');

    if (typeof Worker !== 'undefined') {
      this._jsonDiffWorker = new Worker(
        new URL('./web-worker/diff-service.worker', import.meta.url)
      );
    } else {
      // TODO: This check should probably be done already in directory-selector. The app will not run if the browser does not support web workers
      console.error(
        'The browser does not support service workers! More info: https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API '
      );
      this._jsonDiffWorker = null;
    }
    appSettings.appSettings$
      .pipe(
        distinctUntilChanged(this.appSettingsComparer),
        withLatestFrom(this.selectedVersionNumbers$)
      )
      .subscribe(([appSettings, versions]) => {
        this._currentAppSettings = appSettings;
        if ((versions?.newestVersionNumber ?? -1) >= 0)
          this.setSelectedVersion(versions!.newestVersionNumber);
      });
  }

  public OcadDiffTableView = OcadDiffTableView;
  ngOnInit(): void {
    this.triggerJsonDiff$
      .pipe(
        withLatestFrom(this.newestVersion$, this.oldestVersion$),
        filter(
          ([trigger, current, versioned]) =>
            trigger && current !== null && versioned !== null
        ),
        map(([_, current, versioned]) => {
          // Posting to service worker for it to be processed in a separate thread, allowing rendering of html
          this.progressService.setFileLoadingProgress({
            currentProgress: 80,
            isLoading: true,
          });
          this._jsonDiffWorker?.postMessage({
            newVersion: current,
            oldVersion: versioned,
          } as JsonDiffServiceInput);
        })
      )
      .subscribe();

    if (this._jsonDiffWorker)
      this._jsonDiffWorker.onmessage = ({ data }) => {
        this.handleMessageFromWebWorker(data);
      };
    this.fileWatcherService.currentOcdFileHasChanged$.subscribe(
      async (hasChanged) => {
        if (isNil(hasChanged) || !hasChanged) {
          return;
        }
        await this.setSelectedVersion(0);
      }
    );
  }
  private handleMessageFromWebWorker(data: any): void {
    const currentProgress: number = data as number;
    const diffTable = data as OcadDiffDto;
    if (!isNil(currentProgress) && currentProgress >= 0) {
      const isLoading = data < 100 && data > 0;
      this.progressService.setJsonDiffProgress({
        isLoading,
        currentProgress: isLoading ? data : 0,
      });
      return;
    }
    if (!isNil(diffTable)) {
      this.diffTable$ = of(data as OcadDiffDto);
      this.progressService.setFileLoadingProgress({
        currentProgress: 0,
        isLoading: false,
      });
      return;
    }
  }

  public setSelectedTableView(selectedView: OcadDiffTableView): void {
    this.selectedTableView$.next(selectedView);
  }

  public async setSelectedVersion(selectedVersionNumber: number) {
    const oldestVersionNumber: number | null = this.getVersionNumberToCompare(
      selectedVersionNumber
    );
    this.selectedVersionNumbers$.next({
      newestVersionNumber: selectedVersionNumber,
      oldestVersionNumber,
    });
    this.progressService.setFileLoadingProgress({
      currentProgress: 10,
      isLoading: true,
    });
    const newestVersionName =
      OcadDirectoryHelper.getVersionNameFromVersionNumber(
        selectedVersionNumber
      );
    const selectedOcdFile = await this.provider
      .getOcdFileHandle(newestVersionName)
      .getFile();
    if (selectedVersionNumber === 0)
      this.logger.logInformation(`Loaded map named ${selectedOcdFile.name}`);
    const newestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      selectedOcdFile
    );
    this.progressService.setFileLoadingProgress({
      currentProgress: 10,
      isLoading: true,
    });
    const bboxOfNewest = this.getBoundingBoxOfFeatureCollection(
      newestFeatureCollection
    );
    if (!isNil(bboxOfNewest)) this.bboxOfNewestVersion$.next(bboxOfNewest);
    const newestVersionMetaData: VersionMetaData = {
      versionName:
        selectedVersionNumber === 0 ? selectedOcdFile.name : newestVersionName,
      versionNumber: selectedVersionNumber,
    };
    this.newestVersionMetaData$.next(newestVersionMetaData);

    const ocdFileToCompare = oldestVersionNumber
      ? this.provider.getOcdFileHandle(
          OcadDirectoryHelper.getVersionNameFromVersionNumber(
            oldestVersionNumber
          )
        )
      : null;
    const oldestVersionMetaData: VersionMetaData = {
      versionNumber: oldestVersionNumber ?? 0,
      versionName:
        ocdFileToCompare && !isNil(oldestVersionNumber)
          ? OcadDirectoryHelper.getVersionNameFromVersionNumber(
              oldestVersionNumber
            )
          : '#_empty',
    };
    this.oldestVersionMetaData$.next(oldestVersionMetaData);
    this.progressService.setFileLoadingProgress({
      currentProgress: 20,
      isLoading: true,
    });

    if (!ocdFileToCompare) {
      this.oldestVersion$.next({ features: [], type: 'FeatureCollection' });
      this.newestVersion$.next(newestFeatureCollection);
      this.triggerJsonDiff$.next(true);
      return;
    }
    const oldestFeatureCollection = await this.ocadReader.getGeoJsonFromOcdFile(
      await ocdFileToCompare.getFile()
    );

    this.newestVersion$.next(newestFeatureCollection);
    this.oldestVersion$.next(oldestFeatureCollection);
    this.triggerJsonDiff$.next(true);
  }

  private getBoundingBoxOfFeatureCollection(
    featureCollection: FeatureCollection<Geometry, GeoJsonProperties>
  ): Position[] | null {
    const bboxOfMap = bbox(featureCollection);

    const currentEpsg =
      this._currentAppSettings.georeferencing?.epsgNumber ?? null;
    const minLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
      bboxOfMap[0],
      bboxOfMap[1],
      currentEpsg
    );
    const maxLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
      bboxOfMap[2],
      bboxOfMap[3],
      currentEpsg
    );
    if (isNil(minLatLon) || isNil(maxLatLon)) return null;
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

  private appSettingsComparer(
    previous: AppSettings,
    current: AppSettings
  ): boolean {
    return JSON.stringify(previous) === JSON.stringify(current);
  }
}

interface VersionMetaData {
  versionName: string;
  versionNumber: number;
}
