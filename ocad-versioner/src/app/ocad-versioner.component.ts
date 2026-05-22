import { Component, OnInit } from '@angular/core'
import {
    Position,
} from 'geojson'
import {
    BehaviorSubject,
    Observable,
    distinctUntilChanged,
    of,
    withLatestFrom,
} from 'rxjs'
import { OcadDiffDto } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models'
import { OcadDiffTableView } from './components/ocad-diff-table/ocad-diff-table/ocad-diff-table.component'
import { OcadVersionerProvider } from './ocad-versioner.provider'
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper'
import { isNil } from 'lodash-es'
import { LoggingService } from './services/logging/logging.service'
import { ProgressIndicatorService } from './services/progress-indicator-service/progress-indicator.service'
import { AppProgress } from './services/progress-indicator-service/progress-indicator.service.models'
import { SelectedVersionNumberDto } from './components/ocad-map-viewer/ocad-map-viewer.component'
import { FileWatcherService } from './services/file-watcher/file-watcher.service'
import { AppSettingsService } from './services/app-settings-service/app-settings-service'
import { AppSettings } from './services/app-settings-service/app-settings.models'

@Component({
    selector: 'ocad-versioner',
    templateUrl: './ocad-versioner.component.html',
    styleUrl: './ocad-versioner.component.scss',
})
export class OcadVersionerComponent implements OnInit {
    private _jsonDiffWorker: Worker | null
    private _currentAppSettings: AppSettings = {}
    public newestVersionMetaData$: BehaviorSubject<VersionMetaData | null> =
        new BehaviorSubject<VersionMetaData | null>(null)
    public oldestVersionMetaData$: BehaviorSubject<VersionMetaData | null> =
        new BehaviorSubject<VersionMetaData | null>(null)
    public diffTable$: Observable<OcadDiffDto> = new Observable()
    public selectedTableView$: BehaviorSubject<OcadDiffTableView> =
        new BehaviorSubject<OcadDiffTableView>(OcadDiffTableView.Added)
    public bboxOfNewestVersion$: BehaviorSubject<Position[]> =
        new BehaviorSubject<Position[]>([[]])

    public appProgress$: Observable<AppProgress> =
        this.progressService.getAppProgress()

    public selectedVersionNumbers$: BehaviorSubject<SelectedVersionNumberDto | null> =
        new BehaviorSubject<SelectedVersionNumberDto | null>(null)

    constructor(
        public provider: OcadVersionerProvider,
        private logger: LoggingService,
        private progressService: ProgressIndicatorService,
        private fileWatcherService: FileWatcherService,
        appSettings: AppSettingsService
    ) {
        this.logger.logPageView('ocadversioner.com', 'ocadversioner.com')

        if (typeof Worker !== 'undefined') {
            this._jsonDiffWorker = new Worker(
                new URL('./web-worker/diff-service.worker', import.meta.url)
            )
        } else {
            // TODO: This check should probably be done already in directory-selector. The app will not run if the browser does not support web workers
            console.error(
                'The browser does not support service workers! More info: https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API '
            )
            this._jsonDiffWorker = null
        }
        appSettings.appSettings$
            .pipe(
                distinctUntilChanged(this.appSettingsComparer),
                withLatestFrom(this.selectedVersionNumbers$)
            )
            .subscribe(([appSettings, versions]) => {
                this._currentAppSettings = appSettings
                if ((versions?.newestVersionNumber ?? -1) >= 0)
                    this.setSelectedVersion(versions!.newestVersionNumber)
            })
    }

    public OcadDiffTableView = OcadDiffTableView
    ngOnInit(): void {
        if (this._jsonDiffWorker)
            this._jsonDiffWorker.onmessage = ({ data }) => {
                this.handleMessageFromWebWorker(data)
            }
        this.fileWatcherService.currentOcdFileHasChanged$.subscribe(
            async (hasChanged) => {
                if (isNil(hasChanged) || !hasChanged) {
                    return
                }
                await this.setSelectedVersion(0)
            }
        )
    }
    private handleMessageFromWebWorker(data: any): void {
        if (isNil(data)) return

        if (!isNil(data.progress)) {
            const progress = data.progress
            const isLoading = progress < 100 && progress > 0
            this.progressService.setJsonDiffProgress({
                isLoading,
                currentProgress: isLoading ? progress : 0,
            })
            return
        }

        if (data.error) {
            console.error('Error from web worker:', data.error)
            this.progressService.setFileLoadingProgress({
                currentProgress: 0,
                isLoading: false,
            })
            return
        }

        if (!isNil(data.diff)) {
            this.diffTable$ = of(data.diff)
            if (data.bbox) {
                this.bboxOfNewestVersion$.next(data.bbox)
            }
            this.progressService.setFileLoadingProgress({
                currentProgress: 0,
                isLoading: false,
            })
            return
        }
    }

    public setSelectedTableView(selectedView: OcadDiffTableView): void {
        this.selectedTableView$.next(selectedView)
    }

    public async setSelectedVersion(selectedVersionNumber: number) {
        const oldestVersionNumber: number | null =
            this.getVersionNumberToCompare(selectedVersionNumber)
        this.selectedVersionNumbers$.next({
            newestVersionNumber: selectedVersionNumber,
            oldestVersionNumber,
        })
        this.progressService.setFileLoadingProgress({
            currentProgress: 10,
            isLoading: true,
        })
        const newestVersionName =
            OcadDirectoryHelper.getVersionNameFromVersionNumber(
                selectedVersionNumber
            )
        const selectedOcdFile = await this.provider
            .getOcdFileHandle(newestVersionName)
            .getFile()
        if (selectedVersionNumber === 0)
            this.logger.logInformation(
                `Loaded map named ${selectedOcdFile.name}`
            )

        // Read newest version file as ArrayBuffer
        const newestBuffer = await selectedOcdFile.arrayBuffer()
        this.progressService.setFileLoadingProgress({
            currentProgress: 30,
            isLoading: true,
        })

        const newestVersionMetaData: VersionMetaData = {
            versionName:
                selectedVersionNumber === 0
                    ? selectedOcdFile.name
                    : newestVersionName,
            versionNumber: selectedVersionNumber,
        }
        this.newestVersionMetaData$.next(newestVersionMetaData)

        const ocdFileToCompare = oldestVersionNumber
            ? this.provider.getOcdFileHandle(
                  OcadDirectoryHelper.getVersionNameFromVersionNumber(
                      oldestVersionNumber
                  )
              )
            : null
        const oldestVersionMetaData: VersionMetaData = {
            versionNumber: oldestVersionNumber ?? 0,
            versionName:
                ocdFileToCompare && !isNil(oldestVersionNumber)
                    ? OcadDirectoryHelper.getVersionNameFromVersionNumber(
                          oldestVersionNumber
                      )
                    : '#_empty',
        }
        this.oldestVersionMetaData$.next(oldestVersionMetaData)
        this.progressService.setFileLoadingProgress({
            currentProgress: 50,
            isLoading: true,
        })

        let oldestBuffer: ArrayBuffer | null = null
        if (ocdFileToCompare) {
            const oldestFile = await ocdFileToCompare.getFile()
            oldestBuffer = await oldestFile.arrayBuffer()
        }

        this.progressService.setFileLoadingProgress({
            currentProgress: 80,
            isLoading: true,
        })

        // Transfer ArrayBuffers to web worker using zero-copy Transferable list
        const transferables: Transferable[] = [newestBuffer]
        if (oldestBuffer) {
            transferables.push(oldestBuffer)
        }

        this._jsonDiffWorker?.postMessage({
            newVersionBuffer: newestBuffer,
            oldVersionBuffer: oldestBuffer,
            epsgcode: this._currentAppSettings?.georeferencing?.epsgNumber,
        }, transferables)
    }

    private getVersionNumberToCompare(
        selectedVersionNumber: number
    ): number | null {
        if (selectedVersionNumber === 0)
            return this.provider.getHighestVersionNumber()
        if (selectedVersionNumber === 1) return null
        return selectedVersionNumber - 1
    }

    private appSettingsComparer(
        previous: AppSettings,
        current: AppSettings
    ): boolean {
        return JSON.stringify(previous) === JSON.stringify(current)
    }
}

interface VersionMetaData {
    versionName: string
    versionNumber: number
}
