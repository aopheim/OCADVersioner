import { Injectable } from '@angular/core'
import {
    AppProgress,
    FileLoadingProgress,
    IProgressIndicatorService,
    JsonDiffProgress,
} from './progress-indicator.service.models'
import { BehaviorSubject, Observable, combineLatest, map, timer } from 'rxjs'

@Injectable()
export class ProgressIndicatorService implements IProgressIndicatorService {
    private readonly FileLoadingFraction: number = 1 / 10
    private readonly JsonDiffFraction: number = 9 / 10

    private _jsonDiffProgress: BehaviorSubject<JsonDiffProgress> =
        new BehaviorSubject<JsonDiffProgress>({
            currentProgress: 0,
            isLoading: false,
        })
    private _fileLoadingProgress: BehaviorSubject<FileLoadingProgress> =
        new BehaviorSubject<FileLoadingProgress>({
            currentProgress: 0,
            isLoading: false,
        })

    public getFileLoadingProgress(): Observable<FileLoadingProgress> {
        return this._fileLoadingProgress
    }
    public setFileLoadingProgress(val: FileLoadingProgress) {
        this._fileLoadingProgress.next(val)
    }

    public getJsonDiffProgress(): Observable<JsonDiffProgress> {
        return this._jsonDiffProgress
    }

    public setJsonDiffProgress(val: JsonDiffProgress): void {
        this._jsonDiffProgress.next(val)
    }

    public getAppProgress(): Observable<AppProgress> {
        return combineLatest([
            this.getJsonDiffProgress(),
            this.getFileLoadingProgress(),
        ]).pipe(
            map(([jsonDiff, fileLoadingProgress]) => {
                const isAppLoading =
                    jsonDiff.isLoading || fileLoadingProgress.isLoading
                if (!isAppLoading) {
                    return { currentProgress: 0, isLoading: isAppLoading }
                }

                const totalProgress: number =
                    this.FileLoadingFraction *
                        fileLoadingProgress.currentProgress *
                        (fileLoadingProgress.isLoading ? 1 : 0) +
                    this.JsonDiffFraction *
                        jsonDiff.currentProgress *
                        (jsonDiff.isLoading ? 1 : 0)
                return {
                    currentProgress: totalProgress,
                    isLoading: isAppLoading,
                }
            })
        )
    }

    // Used for debugging
    public getTimer(): Observable<number> {
        return timer(100, 100).pipe(
            map((val) => {
                this._count += 1
                return this._count
            })
        )
    }
    private _count: number = 1
}
