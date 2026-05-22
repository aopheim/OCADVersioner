import { Component, OnDestroy } from '@angular/core'
import { CommonModule } from '@angular/common'
import { TranslateModule } from '@ngx-translate/core'
import {
    BehaviorSubject,
    Observable,
    Subscription,
    distinctUntilChanged,
    of,
    withLatestFrom,
} from 'rxjs'
import {
    FeatureCollection,
    GeoJsonProperties,
    Geometry,
    Position,
} from 'geojson'
import bbox from '@turf/bbox'
import { isNil } from 'lodash-es'
import { OcadVersionerComponentsModule } from '../../components/ocad-versioner-components.module'
import {
    OcadMapFileDroppedDto,
    OcadMapViewerFilesDto,
} from '../../components/ocad-map-viewer/ocad-map-viewer.component'
import { OcadDiffDto } from '../../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models'
import { OcadDiffTableView } from '../../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component'
import { OcadReaderService } from '../../services/ocad-reader-service/ocad-reader-service'
import { ProgressIndicatorService } from '../../services/progress-indicator-service/progress-indicator.service'
import { AppProgress } from '../../services/progress-indicator-service/progress-indicator.service.models'
import { JsonDiffServiceInput } from '../../services/json-diff-service/json-diff-service.models'
import { AppSettingsService } from '../../services/app-settings-service/app-settings-service'
import { AppSettings } from '../../services/app-settings-service/app-settings.models'
import { CoordinatesHelper } from '../../services/coordinates-helper/coordinates-helper.service'

@Component({
    selector: 'diff-viewer-page',
    standalone: true,
    imports: [CommonModule, TranslateModule, OcadVersionerComponentsModule],
    templateUrl: './diff-viewer-page.component.html',
    styleUrl: './diff-viewer-page.component.scss',
})
export class DiffViewerPageComponent implements OnDestroy {
    private _jsonDiffWorker: Worker | null
    private _currentAppSettings: AppSettings = {}
    private _newestVersion: FeatureCollection | null = null
    private _oldestVersion: FeatureCollection | null = null
    private _subscriptions: Subscription = new Subscription()

    public mapFiles: OcadMapViewerFilesDto = {
        newestFile: null,
        oldestFile: null,
    }
    public newestVersionMetaData$: BehaviorSubject<DiffViewerFileMetaData | null> =
        new BehaviorSubject<DiffViewerFileMetaData | null>(null)
    public oldestVersionMetaData$: BehaviorSubject<DiffViewerFileMetaData | null> =
        new BehaviorSubject<DiffViewerFileMetaData | null>(null)
    public diffTable$: Observable<OcadDiffDto | null> = of(null)
    public selectedTableView$: BehaviorSubject<OcadDiffTableView> =
        new BehaviorSubject<OcadDiffTableView>(OcadDiffTableView.Added)
    public bboxOfNewestVersion$: BehaviorSubject<Position[]> =
        new BehaviorSubject<Position[]>([[]])
    public appProgress$: Observable<AppProgress> =
        this.progressService.getAppProgress()

    constructor(
        private ocadReader: OcadReaderService,
        private progressService: ProgressIndicatorService,
        appSettings: AppSettingsService
    ) {
        if (typeof Worker !== 'undefined') {
            this._jsonDiffWorker = new Worker(
                new URL('../../web-worker/diff-service.worker', import.meta.url)
            )
            this._jsonDiffWorker.onmessage = ({ data }) => {
                this.handleMessageFromWebWorker(data)
            }
        } else {
            console.error(
                'The browser does not support service workers! More info: https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API '
            )
            this._jsonDiffWorker = null
        }

        this._subscriptions.add(
            appSettings.appSettings$
                .pipe(
                    distinctUntilChanged(this.appSettingsComparer),
                    withLatestFrom(this.newestVersionMetaData$)
                )
                .subscribe(([settings]) => {
                    this._currentAppSettings = settings
                    this.updateBbox()
                    this.triggerJsonDiff()
                })
        )
    }

    public setSelectedTableView(selectedView: OcadDiffTableView): void {
        this.selectedTableView$.next(selectedView)
    }

    public async setDroppedMapFile(
        droppedFile: OcadMapFileDroppedDto
    ): Promise<void> {
        this.progressService.setFileLoadingProgress({
            currentProgress: 10,
            isLoading: true,
        })

        const featureCollection =
            await this.ocadReader.getGeoJsonFromOcdFile(droppedFile.file)

        if (droppedFile.target === 'newest') {
            this._newestVersion = featureCollection
            this.mapFiles = {
                ...this.mapFiles,
                newestFile: droppedFile.file,
            }
            this.newestVersionMetaData$.next({
                fileName: droppedFile.file.name,
            })
            this.updateBbox()
        } else {
            this._oldestVersion = featureCollection
            this.mapFiles = {
                ...this.mapFiles,
                oldestFile: droppedFile.file,
            }
            this.oldestVersionMetaData$.next({
                fileName: droppedFile.file.name,
            })
        }

        this.progressService.setFileLoadingProgress({
            currentProgress: 70,
            isLoading: false,
        })
        this.triggerJsonDiff()
    }

    private triggerJsonDiff(): void {
        if (!this._newestVersion || !this._oldestVersion) return
        this.progressService.setFileLoadingProgress({
            currentProgress: 80,
            isLoading: true,
        })
        this._jsonDiffWorker?.postMessage({
            newVersion: this._newestVersion,
            oldVersion: this._oldestVersion,
            epsgcode: this._currentAppSettings?.georeferencing?.epsgNumber,
        } as JsonDiffServiceInput)
    }

    private handleMessageFromWebWorker(data: any): void {
        const currentProgress: number = data as number
        const diffTable = data as OcadDiffDto
        if (!isNil(currentProgress) && currentProgress >= 0) {
            const isLoading = data < 100 && data > 0
            this.progressService.setJsonDiffProgress({
                isLoading,
                currentProgress: isLoading ? data : 0,
            })
            return
        }
        if (!isNil(diffTable)) {
            this.diffTable$ = of(data as OcadDiffDto)
            this.progressService.setFileLoadingProgress({
                currentProgress: 0,
                isLoading: false,
            })
        }
    }

    private updateBbox(): void {
        if (!this._newestVersion) return
        const bboxOfNewest = this.getBoundingBoxOfFeatureCollection(
            this._newestVersion
        )
        if (!isNil(bboxOfNewest)) this.bboxOfNewestVersion$.next(bboxOfNewest)
    }

    private getBoundingBoxOfFeatureCollection(
        featureCollection: FeatureCollection<Geometry, GeoJsonProperties>
    ): Position[] | null {
        const bboxOfMap = bbox(featureCollection)

        const currentEpsg =
            this._currentAppSettings.georeferencing?.epsgNumber ?? null
        const minLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
            bboxOfMap[0],
            bboxOfMap[1],
            currentEpsg
        )
        const maxLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
            bboxOfMap[2],
            bboxOfMap[3],
            currentEpsg
        )
        if (isNil(minLatLon) || isNil(maxLatLon)) return null
        return [
            [minLatLon.latitude, minLatLon.longitude],
            [maxLatLon.latitude, maxLatLon.longitude],
        ]
    }

    private appSettingsComparer(
        previous: AppSettings,
        current: AppSettings
    ): boolean {
        return JSON.stringify(previous) === JSON.stringify(current)
    }

    public ngOnDestroy(): void {
        this._subscriptions.unsubscribe()
        this._jsonDiffWorker?.terminate()
    }
}

interface DiffViewerFileMetaData {
    fileName: string
}
