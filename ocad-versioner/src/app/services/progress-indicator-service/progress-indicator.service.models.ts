import { Observable } from 'rxjs'

export interface IProgressIndicatorService {
    getAppProgress(): Observable<AppProgress>
    getJsonDiffProgress(): Observable<JsonDiffProgress>
    setJsonDiffProgress(val: JsonDiffProgress): void
    getFileLoadingProgress(): Observable<FileLoadingProgress>
    setFileLoadingProgress(val: FileLoadingProgress): void
}

export interface JsonDiffProgress {
    isLoading: boolean
    currentProgress: number
}

export interface AppProgress {
    isLoading: boolean
    currentProgress: number
}

export interface FileLoadingProgress {
    isLoading: boolean
    currentProgress: number
}
