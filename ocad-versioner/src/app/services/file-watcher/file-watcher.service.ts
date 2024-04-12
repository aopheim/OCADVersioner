import { Injectable } from '@angular/core';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { Observable, switchMap, timer } from 'rxjs';
import { isNil } from 'lodash-es';

@Injectable()
export class FileWatcherService {
  private _currentFileSize: number = 0;
  private readonly StartFileWatchAfterMs = 5000;
  private readonly FrequencyOfFileCheckInMs = 2000;
  constructor(private provider: OcadVersionerProvider) {}

  // This runs with a high frequency and must be as light as possible!
  public currentOcdFileHasChanged$: Observable<boolean> = timer(
    this.StartFileWatchAfterMs,
    this.FrequencyOfFileCheckInMs
  ).pipe(
    switchMap(async (_) => {
      const currentFile = await this.provider
        .getOcdFileHandle('current')
        ?.getFile();
      const size = currentFile?.size;
      if (isNil(size) || size === this._currentFileSize) {
        return false;
      }
      this._currentFileSize = size;
      return true;
    })
  );
}
